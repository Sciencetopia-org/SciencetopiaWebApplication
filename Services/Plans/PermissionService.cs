using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using Sciencetopia.Data;
using Sciencetopia.Models;
using Sciencetopia.Models.Enums;
using Sciencetopia.DTOs;

namespace Sciencetopia.Services;

public class PermissionService
{
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private const string AllowCohortSharingMetadataKey = "allowCohortSharing";

    private sealed record PlanMetadata(Guid StableId, Guid? CreatorId, string? Privacy, bool AllowCohortSharing);
    private sealed record PlanAccess(PlanRole Role, bool CanView, Guid StableId, bool AllowCohortSharing);

    public PermissionService(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    private static bool ReadAllowCohortSharing(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (doc.RootElement.TryGetProperty(AllowCohortSharingMetadataKey, out var allow)
                || doc.RootElement.TryGetProperty("AllowCohortSharing", out allow))
            {
                return allow.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => bool.TryParse(allow.GetString(), out var parsed) && parsed,
                    _ => false
                };
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private async Task<PlanMetadata> ResolvePlanMetadataAsync(Guid identifier, CancellationToken ct)
    {
        var cacheKey = $"perm:plan-meta:{identifier}";
        if (_cache.TryGetValue<PlanMetadata>(cacheKey, out var cachedMetadata)
            && cachedMetadata != null
            && cachedMetadata.StableId != Guid.Empty)
        {
            return cachedMetadata;
        }

        var plan = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.Id == identifier)
            .Select(p => new
            {
                StableId = p.StableId == Guid.Empty ? p.Id : p.StableId,
                p.CreatorId,
                Privacy = (string?)EF.Property<string>(p, "Privacy"),
                p.MetadataJson
            })
            .FirstOrDefaultAsync(ct);

        if (plan != null)
        {
            var resolved = new PlanMetadata(plan.StableId, plan.CreatorId, plan.Privacy, ReadAllowCohortSharing(plan.MetadataJson));
            _cache.Set(cacheKey, resolved, TimeSpan.FromMinutes(5));
            _cache.Set($"perm:plan-meta:{resolved.StableId}", resolved, TimeSpan.FromMinutes(5));
            return resolved;
        }

        var fallback = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.StableId == identifier)
            .OrderByDescending(p => p.IsCurrent)
            .ThenByDescending(p => p.VersionNumber)
            .Select(p => new
            {
                StableId = p.StableId == Guid.Empty ? p.Id : p.StableId,
                p.CreatorId,
                Privacy = (string?)EF.Property<string>(p, "Privacy"),
                p.MetadataJson
            })
            .FirstOrDefaultAsync(ct);

        if (fallback == null)
        {
            return new PlanMetadata(Guid.Empty, null, null, false);
        }

        var fallbackResolved = new PlanMetadata(fallback.StableId, fallback.CreatorId, fallback.Privacy, ReadAllowCohortSharing(fallback.MetadataJson));
        _cache.Set(cacheKey, fallbackResolved, TimeSpan.FromMinutes(5));
        _cache.Set($"perm:plan-meta:{fallbackResolved.StableId}", fallbackResolved, TimeSpan.FromMinutes(5));
        return fallbackResolved;
    }

    public async Task<Dictionary<Guid, PlanRole>> GetEffectivePlanRolesAsync(string userId, IEnumerable<Guid> planIds, CancellationToken ct = default)
    {
        var requestedIds = (planIds ?? Enumerable.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (requestedIds.Count == 0)
        {
            return new Dictionary<Guid, PlanRole>();
        }

        var metadataRows = await _db.StudyPlans.AsNoTracking()
            .Where(p => requestedIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                StableId = p.StableId == Guid.Empty ? p.Id : p.StableId,
                p.CreatorId,
                Privacy = (string?)EF.Property<string>(p, "Privacy")
            })
            .ToListAsync(ct);

        if (metadataRows.Count == 0)
        {
            return requestedIds.ToDictionary(id => id, _ => PlanRole.Viewer);
        }

        var result = new Dictionary<Guid, PlanRole>(requestedIds.Count);
        var pendingRows = new List<(Guid PlanId, Guid StableId, Guid? CreatorId, string? Privacy)>();

        foreach (var row in metadataRows)
        {
            var cacheKey = $"perm:plan:{row.StableId}:user:{userId}";
            if (_cache.TryGetValue(cacheKey, out PlanRole cached))
            {
                result[row.Id] = cached;
                continue;
            }

            pendingRows.Add((row.Id, row.StableId, row.CreatorId, row.Privacy));
        }

        if (pendingRows.Count == 0)
        {
            return requestedIds.ToDictionary(id => id, id => result.TryGetValue(id, out var role) ? role : PlanRole.Viewer);
        }

        var stableIds = pendingRows.Select(x => x.StableId).Distinct().ToList();
        var userGuid = Guid.TryParse(userId, out var ug) ? ug : Guid.Empty;

        var directRoleByStableId = await _db.UserGroups.AsNoTracking()
            .Where(ug => ug.UserId == userId && (string.IsNullOrEmpty(ug.Status) || ug.Status == "Active" || ug.Status == "active"))
            .Join(_db.GroupPlanEnrollments.AsNoTracking().Where(e => e.Status == "Active"),
                ug => ug.GroupId,
                e => e.GroupId,
                (ug, e) => new { e.StudyPlanStableId, e.Role })
            .Where(x => stableIds.Contains(x.StudyPlanStableId))
            .GroupBy(x => x.StudyPlanStableId)
            .Select(g => new
            {
                StableId = g.Key,
                Role = g.Max(x => x.Role)
            })
            .ToDictionaryAsync(x => x.StableId, x => x.Role, ct);

        var groupLinkedRows = await _db.UserGroups.AsNoTracking()
            .Where(gr => gr.UserId == userId && (string.IsNullOrEmpty(gr.Status) || gr.Status == "Active" || gr.Status == "active"))
            .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                gr => gr.GroupId,
                gp => gp.StudyGroupId,
                (gr, gp) => gp.StudyPlanStableId)
            .Where(stableId => stableIds.Contains(stableId))
            .Distinct()
            .ToListAsync(ct);
        var enrolledRows = await _db.UserGroups.AsNoTracking()
            .Where(gr => gr.UserId == userId && (string.IsNullOrEmpty(gr.Status) || gr.Status == "Active" || gr.Status == "active"))
            .Join(_db.GroupPlanEnrollments.AsNoTracking().Where(e => e.Status == "Active"),
                gr => gr.GroupId,
                e => e.GroupId,
                (gr, e) => e.StudyPlanStableId)
            .Where(stableId => stableIds.Contains(stableId))
            .Distinct()
            .ToListAsync(ct);
        groupLinkedRows.AddRange(enrolledRows);
        var groupLinkedStableIds = groupLinkedRows.ToHashSet();

        foreach (var row in pendingRows)
        {
            var role = PlanRole.Viewer;
            var canView = false;

            if (row.CreatorId.HasValue && row.CreatorId.Value == userGuid)
            {
                role = PlanRole.Owner;
                canView = true;
            }
            else if (directRoleByStableId.TryGetValue(row.StableId, out var directRole))
            {
                role = directRole;
                canView = true;
            }
            else if (groupLinkedStableIds.Contains(row.StableId))
            {
                role = PlanRole.Viewer;
                canView = true;
            }
            else if (string.Equals(row.Privacy, "public", StringComparison.OrdinalIgnoreCase))
            {
                role = PlanRole.Viewer;
                canView = true;
            }

            if (canView)
            {
                result[row.PlanId] = SetCache($"perm:plan:{row.StableId}:user:{userId}", role);
            }
        }

        return requestedIds.ToDictionary(id => id, id => result.TryGetValue(id, out var role) ? role : PlanRole.Viewer);
    }

    public async Task<PlanRole> GetEffectivePlanRoleAsync(string userId, Guid planId, CancellationToken ct = default)
    {
        var access = await GetEffectivePlanAccessAsync(userId, planId, ct);
        return access.Role;
    }

    private async Task<PlanAccess> GetEffectivePlanAccessAsync(string userId, Guid planId, CancellationToken ct = default)
    {
        var metadata = await ResolvePlanMetadataAsync(planId, ct);
        if (metadata.StableId == Guid.Empty)
        {
            return new PlanAccess(PlanRole.Viewer, false, Guid.Empty, false);
        }

        var cacheKey = $"perm:plan:{metadata.StableId}:user:{userId}";
        if (_cache.TryGetValue(cacheKey, out PlanRole cached))
        {
            return new PlanAccess(cached, true, metadata.StableId, metadata.AllowCohortSharing);
        }

        var userGuid = Guid.TryParse(userId, out var ug) ? ug : Guid.Empty;

        if (metadata.CreatorId.HasValue && metadata.CreatorId.Value == userGuid)
        {
            return new PlanAccess(SetCache(cacheKey, PlanRole.Owner), true, metadata.StableId, metadata.AllowCohortSharing);
        }

        var direct = await _db.UserGroups.AsNoTracking()
            .Where(ug => ug.UserId == userId && (string.IsNullOrEmpty(ug.Status) || ug.Status == "Active" || ug.Status == "active"))
            .Join(_db.GroupPlanEnrollments.AsNoTracking().Where(e => e.Status == "Active" && e.StudyPlanStableId == metadata.StableId),
                ug => ug.GroupId,
                e => e.GroupId,
                (ug, e) => (PlanRole?)e.Role)
            .OrderByDescending(role => role)
            .FirstOrDefaultAsync(ct);

        if (direct.HasValue)
        {
            return new PlanAccess(SetCache(cacheKey, direct.Value), true, metadata.StableId, metadata.AllowCohortSharing);
        }

        var hasGroupLink = await _db.UserGroups.AsNoTracking()
            .Where(gr => gr.UserId == userId && (string.IsNullOrEmpty(gr.Status) || gr.Status == "Active" || gr.Status == "active"))
            .Join(_db.StudyGroupStudyPlans.AsNoTracking(), gr => gr.GroupId, gp => gp.StudyGroupId, (gr, gp) => gp.StudyPlanStableId)
            .AnyAsync(stable => stable == metadata.StableId, ct);
        if (!hasGroupLink)
        {
            hasGroupLink = await _db.UserGroups.AsNoTracking()
                .Where(gr => gr.UserId == userId && (string.IsNullOrEmpty(gr.Status) || gr.Status == "Active" || gr.Status == "active"))
                .Join(_db.GroupPlanEnrollments.AsNoTracking().Where(e => e.Status == "Active"), gr => gr.GroupId, e => e.GroupId, (gr, e) => e.StudyPlanStableId)
                .AnyAsync(stable => stable == metadata.StableId, ct);
        }

        if (hasGroupLink)
        {
            return new PlanAccess(SetCache(cacheKey, PlanRole.Viewer), true, metadata.StableId, metadata.AllowCohortSharing);
        }

        if (string.Equals(metadata.Privacy, "public", StringComparison.OrdinalIgnoreCase))
        {
            return new PlanAccess(SetCache(cacheKey, PlanRole.Viewer), true, metadata.StableId, metadata.AllowCohortSharing);
        }

        return new PlanAccess(PlanRole.Viewer, false, metadata.StableId, metadata.AllowCohortSharing);
    }

    private PlanRole SetCache(string key, PlanRole role)
    {
        _cache.Set(key, role, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(90)
        });
        return role;
    }

    public Task<bool> CanReadAsync(string userId, Guid planId, CancellationToken ct = default)
        => CheckAsync(userId, planId, r => r >= PlanRole.Viewer, ct);

    public Task<bool> CanCommentAsync(string userId, Guid planId, CancellationToken ct = default)
        => CheckAsync(userId, planId, r => r >= PlanRole.Commenter, ct);

    public Task<bool> CanEditAsync(string userId, Guid planId, CancellationToken ct = default)
        => CheckAsync(userId, planId, r => r >= PlanRole.Editor || r == PlanRole.Owner, ct);

    public Task<bool> CanShareAsync(string userId, Guid planId, CancellationToken ct = default)
        => CheckAsync(userId, planId, r => r == PlanRole.Owner, ct);

    private async Task<bool> CheckAsync(string userId, Guid planId, Func<PlanRole, bool> predicate, CancellationToken ct)
    {
        var access = await GetEffectivePlanAccessAsync(userId, planId, ct);
        return access.CanView && predicate(access.Role);
    }

    public async Task<bool> CanAdoptPlanAsync(string userId, Guid planId, CancellationToken ct = default)
    {
        var access = await GetEffectivePlanAccessAsync(userId, planId, ct);
        if (!access.CanView)
        {
            return false;
        }

        return access.AllowCohortSharing || access.Role >= PlanRole.Editor || access.Role == PlanRole.Owner;
    }

    public async Task<bool> CanAdoptPlanToGroupAsync(string userId, Guid planId, Guid groupId, CancellationToken ct = default)
    {
        if (!await CanAdoptPlanAsync(userId, planId, ct))
        {
            return false;
        }

        return await IsGroupManagerAsync(userId, groupId, ct);
    }

    public async Task InvalidateAsync(string userId, Guid planId, CancellationToken ct = default)
    {
        _cache.Remove($"perm:plan:{planId}:user:{userId}");
        var metadata = await ResolvePlanMetadataAsync(planId, ct);
        if (metadata.StableId != Guid.Empty)
        {
            _cache.Remove($"perm:plan:{metadata.StableId}:user:{userId}");
        }
    }

    public async Task InvalidatePlanMetadataAsync(Guid planId, CancellationToken ct = default)
    {
        _cache.Remove($"perm:plan-meta:{planId}");
        var metadata = await ResolvePlanMetadataAsync(planId, ct);
        if (metadata.StableId != Guid.Empty)
        {
            _cache.Remove($"perm:plan-meta:{metadata.StableId}");
        }
    }

    private async Task<bool> IsGroupManagerAsync(string userId, Guid groupId, CancellationToken ct)
    {
        return await _db.UserGroups.AsNoTracking()
            .AnyAsync(x =>
                x.GroupId == groupId
                && x.UserId == userId
                && (string.IsNullOrEmpty(x.Status) || x.Status == "Active" || x.Status == "active")
                && x.Role >= GroupRole.Admin, ct);
    }

    // Returns an aggregated boolean permissions view for a given user/plan/cohort.
    // Plan permissions are derived from plan role; Cohort permissions are decoupled from plan
    // and decided by cohort scope (group-scoped vs standalone) and creator/manager roles.
    public async Task<EffectivePermissionsDto> GetEffectivePermissionsAsync(string userId, Guid planId, Guid? cohortId = null, CancellationToken ct = default)
    {
        // Plan-level permissions
        var access = await GetEffectivePlanAccessAsync(userId, planId, ct);
        var role = access.Role;
        var canView = access.CanView;
        var canComment = role >= PlanRole.Commenter;
        var canEdit = role >= PlanRole.Editor || role == PlanRole.Owner;
        var canPublish = role == PlanRole.Owner;
        var canAdoptPlanToCohort = canView && (access.AllowCohortSharing || canEdit);

        // Cohort-level permissions (decoupled from plan role)
        bool cohortManage = false, cohortInvite = false;
        string? cohortPermission = null;
        if (cohortId.HasValue)
        {
            // Load cohort scope
            var info = await _db.Cohorts.AsNoTracking()
                .Where(c => c.Id == cohortId.Value)
                .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                    c => c.StudyGroupStudyPlanId,
                    sgsp => sgsp.Id,
                    (c, sgsp) => new
                    {
                        StudyGroupId = (Guid?)sgsp.StudyGroupId,
                        c.CreatedBy,
                        sgsp.Permission
                    })
                .FirstOrDefaultAsync(ct);

            if (info != null)
            {
                cohortPermission = NormalizeCohortPermission(info.Permission);
                if (info.StudyGroupId.HasValue && info.StudyGroupId.Value != Guid.Empty)
                {
                    // Group managers can administer cohorts. A group-plan "admin" permission
                    // grants the same cohort-management capability to active group members.
                    var isMember = await IsGroupMemberAsync(userId, info.StudyGroupId.Value, ct);
                    var isManager = isMember && await IsGroupManagerAsync(userId, info.StudyGroupId.Value, ct);
                    if (isManager || (isMember && cohortPermission == "admin"))
                    {
                        cohortManage = true;
                        cohortInvite = true;
                    }
                }
                else
                {
                    // Non group-scoped cohorts are not supported for management in the strong coupling model
                    cohortManage = false;
                    cohortInvite = false;
                }
            }
        }

        return new EffectivePermissionsDto
        {
            Role = role.ToString(),
            CanView = canView,
            CanComment = canView && canComment,
            CanEdit = canView && canEdit,
            CanPublish = canView && canPublish,
            AllowCohortSharing = access.AllowCohortSharing,
            CanAdoptPlanToCohort = canAdoptPlanToCohort,
            CohortManage = cohortManage,
            CohortInvite = cohortInvite,
            CohortPermission = cohortPermission
        };
    }

    private static string NormalizeCohortPermission(string? permission)
    {
        var value = (permission ?? "view").Trim().ToLowerInvariant();
        return value switch
        {
            "admin" => "admin",
            "edit" or "editable" => "edit",
            "comment" => "comment",
            _ => "view"
        };
    }

    private async Task<bool> IsGroupMemberAsync(string userId, Guid groupId, CancellationToken ct)
    {
        return await _db.UserGroups.AsNoTracking()
            .AnyAsync(x =>
                x.GroupId == groupId
                && x.UserId == userId
                && (string.IsNullOrEmpty(x.Status) || x.Status == "Active" || x.Status == "active"), ct);
    }
}
