using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Data.SqlClient;
using Sciencetopia.Data;
using Sciencetopia.Models;
using Sciencetopia.Models.Enums;
using Sciencetopia.DTOs;

namespace Sciencetopia.Services;

    public class PermissionService
    {
        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;

    public PermissionService(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    private static bool IsMissingObjectException(Exception ex, params string[] objectNames)
    {
        if (ex is not SqlException sqlEx || sqlEx.Number != 208) return false;
        if (objectNames == null || objectNames.Length == 0) return true;
        return objectNames.Any(n => sqlEx.Message.Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<(Guid stableId, Guid? creatorId, string? privacy)> ResolvePlanMetadataAsync(Guid identifier, CancellationToken ct)
    {
        var plan = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.Id == identifier)
            .Select(p => new
            {
                StableId = p.StableId == Guid.Empty ? p.Id : p.StableId,
                p.CreatorId,
                Privacy = (string?)EF.Property<string>(p, "Privacy")
            })
            .FirstOrDefaultAsync(ct);

        if (plan != null)
        {
            return (plan.StableId, plan.CreatorId, plan.Privacy);
        }

        var fallback = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.StableId == identifier)
            .OrderByDescending(p => p.IsCurrent)
            .ThenByDescending(p => p.VersionNumber)
            .Select(p => new
            {
                StableId = p.StableId == Guid.Empty ? p.Id : p.StableId,
                p.CreatorId,
                Privacy = (string?)EF.Property<string>(p, "Privacy")
            })
            .FirstOrDefaultAsync(ct);

        return fallback == null ? (Guid.Empty, null, null) : (fallback.StableId, fallback.CreatorId, fallback.Privacy);
    }

    public async Task<PlanRole> GetEffectivePlanRoleAsync(string userId, Guid planId, CancellationToken ct = default)
    {
        var (stableId, creatorId, privacy) = await ResolvePlanMetadataAsync(planId, ct);
        if (stableId == Guid.Empty)
        {
            return PlanRole.Viewer;
        }

        var cacheKey = $"perm:plan:{stableId}:user:{userId}";
        if (_cache.TryGetValue(cacheKey, out PlanRole cached))
            return cached;

        // Owner via StudyPlans.CreatorId (GUID stored)
        var userGuid = Guid.TryParse(userId, out var ug) ? ug : Guid.Empty;

        // a) Owner
        if (creatorId.HasValue && creatorId.Value == userGuid)
        {
            return SetCache(cacheKey, PlanRole.Owner);
        }

        PlanRole best = PlanRole.Viewer;

        // b) Direct user role
        var direct = PlanRole.Viewer;
        try
        {
            direct = await _db.StudyPlanUserRoles
                .Where(r => r.PlanStableId == stableId && r.UserId == userId)
                .Select(r => r.Role)
                .FirstOrDefaultAsync(ct);
        }
        catch (Exception ex) when (IsMissingObjectException(ex, "StudyPlanUserRoles"))
        {
            direct = PlanRole.Viewer;
        }
        if (direct > best) best = direct;

        // c) Group visibility: user is member of groups with roles; plan linked to those groups
        var hasGroupLink = false;
        try
        {
            hasGroupLink = await _db.UserGroups.AsNoTracking()
                .Where(gr => gr.UserId == userId && (string.IsNullOrEmpty(gr.Status) || gr.Status == "Active" || gr.Status == "active"))
                .Join(_db.StudyGroupStudyPlans.AsNoTracking(), gr => gr.GroupId, gp => gp.StudyGroupId, (gr, gp) => gp.StudyPlanStableId)
                .AnyAsync(stable => stable == stableId, ct);
        }
        catch (Exception ex) when (IsMissingObjectException(ex, "UserGroups", "StudyGroupStudyPlans"))
        {
            hasGroupLink = false;
        }
        if (hasGroupLink && best < PlanRole.Viewer)
            best = PlanRole.Viewer; // default visibility level from group

        // d) Privacy fallback (Public → Viewer)
        var privacyLevel = privacy?.ToLowerInvariant();
        if (privacyLevel == "public" && best < PlanRole.Viewer)
            best = PlanRole.Viewer;

        return SetCache(cacheKey, best);
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
        var role = await GetEffectivePlanRoleAsync(userId, planId, ct);
        return predicate(role);
    }

    public async Task InvalidateAsync(string userId, Guid planId, CancellationToken ct = default)
    {
        _cache.Remove($"perm:plan:{planId}:user:{userId}");
        var (stableId, _, _) = await ResolvePlanMetadataAsync(planId, ct);
        if (stableId != Guid.Empty)
        {
            _cache.Remove($"perm:plan:{stableId}:user:{userId}");
        }
    }

    // Returns an aggregated boolean permissions view for a given user/plan/cohort.
    // Plan permissions are derived from plan role; Cohort permissions are decoupled from plan
    // and decided by cohort scope (group-scoped vs standalone) and creator/manager roles.
    public async Task<EffectivePermissionsDto> GetEffectivePermissionsAsync(string userId, Guid planId, Guid? cohortId = null, CancellationToken ct = default)
    {
        // Plan-level permissions
        var role = await GetEffectivePlanRoleAsync(userId, planId, ct);
        var canView = role >= PlanRole.Viewer;
        var canComment = role >= PlanRole.Commenter;
        var canEdit = role >= PlanRole.Editor || role == PlanRole.Owner;
        var canPublish = role == PlanRole.Owner;

        // Cohort-level permissions (decoupled from plan role)
        bool cohortManage = false, cohortInvite = false;
        if (cohortId.HasValue)
        {
            // Load cohort scope
            var info = await _db.Cohorts.AsNoTracking()
                .Where(c => c.Id == cohortId.Value)
                .Select(c => new { c.StudyGroupId, c.CreatedBy })
                .FirstOrDefaultAsync(ct);

            if (info != null)
            {
                if (info.StudyGroupId.HasValue && info.StudyGroupId.Value != Guid.Empty)
                {
                    // Group-scoped cohort: group managers manage/invite
                    var isManager = false;
                    try
                    {
                        isManager = await _db.UserGroups.AsNoTracking()
                            .AnyAsync(x =>
                                x.GroupId == info.StudyGroupId.Value
                                && x.UserId == userId
                                && (string.IsNullOrEmpty(x.Status) || x.Status == "Active" || x.Status == "active")
                                && x.Role >= Models.Enums.GroupRole.Admin, ct);
                    }
                    catch (Exception ex) when (IsMissingObjectException(ex, "UserGroups"))
                    {
                        isManager = false;
                    }
                    if (isManager)
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
            CanView = canView,
            CanComment = canComment,
            CanEdit = canEdit,
            CanPublish = canPublish,
            CohortManage = cohortManage,
            CohortInvite = cohortInvite
        };
    }
}
