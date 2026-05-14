using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Models;
using Sciencetopia.Models.Enums;
using Sciencetopia.Services;
using System.Security.Claims;
using Sciencetopia.Services.Progress;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using Sciencetopia.Services.Plans;

namespace Sciencetopia.Controllers.StudyPlan
{
    [ApiController]
    [Route("api/StudyPlans")] // PascalCase
    public class StudyPlansController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly PermissionService _perm;
        private readonly IResourceProgressService _progress;
        private readonly IMemoryCache _cache;
        private readonly ILogger<StudyPlansController> _logger;
        private readonly IPersonalPlanEnrollmentService _personalGroups;
        private static readonly TimeSpan StudyPlansVisibilityCacheTtl = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan StudyPlansResponseCacheTtl = TimeSpan.FromSeconds(30);

        private static string GetStudyPlanListVersionCacheKey(string userId)
            => $"studyplans:list-version:{userId}";

        public StudyPlansController(
            ApplicationDbContext db,
            PermissionService perm,
            IResourceProgressService progress,
            IMemoryCache cache,
            ILogger<StudyPlansController> logger,
            IPersonalPlanEnrollmentService personalGroups)
        {
            _db = db;
            _perm = perm;
            _progress = progress;
            _cache = cache;
            _logger = logger;
            _personalGroups = personalGroups;
        }

        private static string SetAllowCohortSharing(string? metadataJson, bool allow)
        {
            var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(metadataJson))
            {
                try
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(metadataJson);
                    if (existing != null)
                    {
                        foreach (var kv in existing)
                        {
                            metadata[kv.Key] = kv.Value.Clone();
                        }
                    }
                }
                catch (JsonException)
                {
                }
            }

            metadata["allowCohortSharing"] = allow;
            return JsonSerializer.Serialize(metadata);
        }

        private async Task<List<Guid>> GetGroupSharedStableIdsAsync(List<Guid> groupIds)
        {
            if (groupIds == null || groupIds.Count == 0) return new List<Guid>();
            return await _db.StudyGroupStudyPlans.AsNoTracking()
                .Where(gp => groupIds.Contains(gp.StudyGroupId))
                .Select(gp => gp.StudyPlanStableId)
                .Distinct()
                .ToListAsync();
        }

        private async Task<List<Guid>> GetEnrolledStableIdsAsync(string userId)
        {
            var result = new HashSet<Guid>();

            var personalRows = await _db.UserGroups.AsNoTracking()
                .Where(ug => ug.UserId == userId && ug.Status == "Active")
                .Join(_db.GroupPlanEnrollments.AsNoTracking().Where(e => e.Status == "Active"),
                    ug => ug.GroupId,
                    e => e.GroupId,
                    (ug, e) => e.StudyPlanStableId)
                .Distinct()
                .ToListAsync();
            result.UnionWith(personalRows.Where(x => x != Guid.Empty));

            var cohortRows = await _db.UserGroups.AsNoTracking()
                .Where(ug => ug.UserId == userId && ug.Status == "Active")
                .Join(_db.Cohorts.AsNoTracking().Where(c => c.Status == "Active"),
                    ug => ug.GroupId,
                    c => c.Id,
                    (ug, c) => c)
                .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                    c => c.StudyGroupStudyPlanId,
                    sgsp => sgsp.Id,
                    (c, sgsp) => sgsp.StudyPlanStableId)
                .Distinct()
                .ToListAsync();
            result.UnionWith(cohortRows.Where(x => x != Guid.Empty));

            return result.ToList();
        }

        private async Task<bool> HasActivePlanEnrollmentByStableIdAsync(string userId, Guid stableId)
        {
            return await _db.UserGroups.AsNoTracking()
                .Where(ug => ug.UserId == userId && ug.Status == "Active")
                .Join(_db.Cohorts.AsNoTracking().Where(c => c.Status == "Active"),
                    ug => ug.GroupId,
                    c => c.Id,
                    (ug, c) => c)
                .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                    c => c.StudyGroupStudyPlanId,
                    sgsp => sgsp.Id,
                    (c, sgsp) => sgsp.StudyPlanStableId)
                .AnyAsync(planStableId => planStableId == stableId);
        }

        private async Task<List<Guid>> GetVisibleStableIdsAsync(string userId, Guid userGuid)
        {
            var cacheKey = $"studyplans:visible-stableids:{userId}";
            if (_cache.TryGetValue(cacheKey, out List<Guid>? cached) && cached != null)
            {
                return cached;
            }

            var visible = new HashSet<Guid>();

            var createdStableIds = await _db.StudyPlans.AsNoTracking()
                .Where(p => (userGuid != Guid.Empty && p.CreatorId == userGuid) || p.CreatedBy == userId)
                .Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
                .Distinct()
                .ToListAsync();
            visible.UnionWith(createdStableIds.Where(x => x != Guid.Empty));

            var groupIds = await GetActiveGroupIdsForUserAsync(userId);
            if (groupIds.Count > 0)
            {
                var groupSharedStableIds = await GetGroupSharedStableIdsAsync(groupIds);
                visible.UnionWith(groupSharedStableIds.Where(x => x != Guid.Empty));
            }

            var enrolledStableIds = await GetEnrolledStableIdsAsync(userId);
            visible.UnionWith(enrolledStableIds.Where(x => x != Guid.Empty));

            var result = visible.ToList();
            _cache.Set(cacheKey, result, StudyPlansVisibilityCacheTtl);
            return result;
        }

        // User-group membership source-of-truth: Groups.UserGroups
        private async Task<List<Guid>> GetActiveGroupIdsForUserAsync(string userId)
        {
            return await _db.UserGroups.AsNoTracking()
                .Where(gr => gr.UserId == userId && (string.IsNullOrEmpty(gr.Status) || gr.Status == "Active" || gr.Status == "active"))
                .Select(gr => gr.GroupId)
                .Distinct()
                .ToListAsync();
        }

        private async Task<bool> IsGroupMemberAsync(string userId, Guid groupId)
        {
            return await _db.UserGroups.AsNoTracking()
                .AnyAsync(gr => gr.GroupId == groupId
                    && gr.UserId == userId
                    && (string.IsNullOrEmpty(gr.Status) || gr.Status == "Active" || gr.Status == "active"));
        }

        private static bool MatchesProgressFilter(double progress, string? progressStatus)
        {
            return (progressStatus ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "inprogress" => progress < 99.999,
                "completed" => progress >= 99.999,
                _ => true
            };
        }

        private async Task<object> BuildListFallbackAsync(string subjectUserId, int page, int pageSize, string? q, string? sort, string? progressStatus)
        {
            var subjectGuid = Guid.TryParse(subjectUserId, out var parsedSubjectId) ? parsedSubjectId : Guid.Empty;

            var enrolledStableIds = await _db.UserGroups.AsNoTracking()
                .Where(ug => ug.UserId == subjectUserId && (string.IsNullOrEmpty(ug.Status) || ug.Status == "Active" || ug.Status == "active"))
                .Join(_db.GroupPlanEnrollments.AsNoTracking().Where(e => e.Status == "Active"),
                    ug => ug.GroupId,
                    e => e.GroupId,
                    (ug, e) => e.StudyPlanStableId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToListAsync();

            var query = _db.StudyPlans.AsNoTracking()
                .Where(p =>
                    (subjectGuid != Guid.Empty && p.CreatorId == subjectGuid)
                    || p.CreatedBy == subjectUserId
                    || enrolledStableIds.Contains(p.StableId)
                    || (p.StableId == Guid.Empty && enrolledStableIds.Contains(p.Id)));

            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(p => p.Title.Contains(q));
            }

            query = sort switch
            {
                "createdDesc" => query.OrderByDescending(p => p.CreatedDate),
                "createdAsc" => query.OrderBy(p => p.CreatedDate),
                "updatedDesc" => query.OrderByDescending(p => p.UpdatedDate),
                "updatedAsc" => query.OrderBy(p => p.UpdatedDate),
                "lastStudiedAsc" => query.OrderBy(p => p.LastStudiedAt ?? DateTime.MinValue),
                _ => query.OrderByDescending(p => p.LastStudiedAt ?? p.UpdatedDate)
            };

            var total = await query.CountAsync();
            var slice = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new
                {
                    p.Id,
                    StableId = p.StableId == Guid.Empty ? p.Id : p.StableId,
                    p.VersionNumber,
                    p.IsCurrent,
                    p.Status,
                    p.Title,
                    p.Description,
                    p.CreatedDate,
                    p.UpdatedDate,
                    p.LastStudiedAt
                })
                .ToListAsync();

            var items = slice.Select(p => new
            {
                id = p.Id,
                stableId = p.StableId,
                versionNumber = p.VersionNumber,
                latestVersionNumber = p.VersionNumber,
                currentVersionNumber = p.VersionNumber,
                isCurrent = p.IsCurrent,
                status = p.Status,
                title = p.Title,
                description = p.Description,
                updatedAt = p.UpdatedDate,
                lastStudiedAt = p.LastStudiedAt,
                createdAt = p.CreatedDate,
                hasUpgrade = false,
                role = PlanRole.Viewer.ToString(),
                progress = 0.0,
                advancedProgress = 0.0
            })
            .Where(p => MatchesProgressFilter(p.progress, progressStatus))
            .ToList();

            return new { total, page, pageSize, items };
        }

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? q = null,
            [FromQuery] string? sort = null,
            [FromQuery] string? scope = null,
            [FromQuery] string? progressStatus = null,
            [FromQuery] string? targetUserId = null)
        {
            var totalSw = Stopwatch.StartNew();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var subjectUserId = string.IsNullOrWhiteSpace(targetUserId) ? userId : targetUserId;
            var normalizedScope = string.IsNullOrWhiteSpace(scope) ? "mine" : scope.Trim().ToLowerInvariant();
            var listVersion = _cache.TryGetValue<long>(GetStudyPlanListVersionCacheKey(subjectUserId), out var cachedVersion)
                ? cachedVersion
                : 0L;

            var normalizedProgressStatus = string.IsNullOrWhiteSpace(progressStatus) ? "all" : progressStatus.Trim().ToLowerInvariant();
            var responseCacheKey = $"studyplans:list:{userId}:subject:{subjectUserId}:v:{listVersion}:scope:{normalizedScope}:progress:{normalizedProgressStatus}:page:{page}:size:{pageSize}:q:{q ?? string.Empty}:sort:{sort ?? string.Empty}";
            if (_cache.TryGetValue(responseCacheKey, out object? cachedPayload) && cachedPayload != null)
            {
                totalSw.Stop();
                _logger.LogInformation(
                    "StudyPlans.List cache hit. userId={UserId} subjectUserId={SubjectUserId} scope={Scope} page={Page} pageSize={PageSize} totalMs={TotalMs}",
                    userId,
                    subjectUserId,
                    normalizedScope,
                    page,
                    pageSize,
                    totalSw.ElapsedMilliseconds);
                return Ok(cachedPayload);
            }

            try
            {
            var ug = Guid.TryParse(subjectUserId, out var parsed) ? parsed : Guid.Empty;
            var visibilitySw = Stopwatch.StartNew();
            var stableSet = await GetVisibleStableIdsAsync(subjectUserId, ug);
            visibilitySw.Stop();

            var baseQ = _db.StudyPlans.AsNoTracking().Where(p =>
                stableSet.Contains(p.StableId)
                || (p.StableId == Guid.Empty && stableSet.Contains(p.Id)));

            if (normalizedScope != "mine")
            {
                baseQ = baseQ.Where(p =>
                    EF.Property<string>(p, "Privacy") == "public"
                    || EF.Property<string>(p, "Privacy") == "Public"
                    || stableSet.Contains(p.StableId)
                    || (p.StableId == Guid.Empty && stableSet.Contains(p.Id)));
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                baseQ = baseQ.Where(p => p.Title.Contains(q));
            }

            var requiresProgressShaping = normalizedProgressStatus is "inprogress" or "completed"
                || string.Equals(sort, "progressDesc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(sort, "progressAsc", StringComparison.OrdinalIgnoreCase);

            baseQ = sort switch
            {
                "createdDesc" => baseQ.OrderByDescending(p => p.CreatedDate),
                "createdAsc" => baseQ.OrderBy(p => p.CreatedDate),
                "updatedDesc" => baseQ.OrderByDescending(p => p.UpdatedDate),
                "updatedAsc" => baseQ.OrderBy(p => p.UpdatedDate),
                "lastStudiedAsc" => baseQ.OrderBy(p => p.LastStudiedAt ?? DateTime.MinValue),
                _ => baseQ.OrderByDescending(p => p.LastStudiedAt ?? p.UpdatedDate)
            };

            var countSw = Stopwatch.StartNew();
            var total = requiresProgressShaping ? 0 : await baseQ.CountAsync();
            countSw.Stop();

            // Project only Id and Title for speed
            var sliceSw = Stopwatch.StartNew();
            var rawRowsQuery = requiresProgressShaping
                ? baseQ
                : baseQ.Skip((page - 1) * pageSize).Take(pageSize);

            var slice = await rawRowsQuery
                .Select(p => new
                {
                    p.Id,
                    p.StableId,
                    p.VersionNumber,
                    p.IsCurrent,
                    p.Status,
                    p.Title,
                    p.Description,
                    p.CreatedDate,
                    p.UpdatedDate,
                    p.LastStudiedAt
                })
                .ToListAsync();
            sliceSw.Stop();

            var stableIds = slice.Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId).Distinct().ToList();

            var aggregatesSw = Stopwatch.StartNew();
            var aggregates = await _db.StudyPlans.AsNoTracking()
                .Where(p => stableIds.Contains(p.StableId) || (p.StableId == Guid.Empty && stableIds.Contains(p.Id)))
                .GroupBy(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
                .Select(g => new
                {
                    StableId = g.Key,
                    LatestVersionNumber = g.Max(p => p.VersionNumber),
                    CurrentVersionNumber = g.Where(p => p.IsCurrent).Select(p => (int?)p.VersionNumber).FirstOrDefault()
                })
                .ToListAsync();
            aggregatesSw.Stop();

            var aggregatesDict = aggregates.ToDictionary(a => a.StableId, a => a);
            var progressSw = Stopwatch.StartNew();
            var progressByStableId = new Dictionary<Guid, Sciencetopia.DTOs.UserPlanProgressDto>();
            try
            {
                progressByStableId = await _progress.GetPlanProgressByPlanIdsAsync(subjectUserId, stableIds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "StudyPlans.List progress fallback. userId={UserId} subjectUserId={SubjectUserId} planCount={PlanCount}",
                    userId,
                    subjectUserId,
                    stableIds.Count);
            }
            progressSw.Stop();

            var permSw = Stopwatch.StartNew();
            var rolesByPlanId = new Dictionary<Guid, PlanRole>();
            try
            {
                rolesByPlanId = await _perm.GetEffectivePlanRolesAsync(userId, slice.Select(p => p.Id));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "StudyPlans.List permission fallback. userId={UserId} subjectUserId={SubjectUserId} planCount={PlanCount}",
                    userId,
                    subjectUserId,
                    slice.Count);
            }
            permSw.Stop();

            var items = slice.Select(p =>
            {
                var stableId = p.StableId == Guid.Empty ? p.Id : p.StableId;
                var agg = aggregatesDict.TryGetValue(stableId, out var entry)
                    ? entry
                    : new { StableId = stableId, LatestVersionNumber = p.VersionNumber, CurrentVersionNumber = (int?) (p.IsCurrent ? p.VersionNumber : (int?)null) };

                var currentVersionNumber = agg.CurrentVersionNumber ?? agg.LatestVersionNumber;
                var hasUpgrade = !p.IsCurrent && p.VersionNumber < agg.LatestVersionNumber;

                var role = rolesByPlanId.TryGetValue(p.Id, out var resolvedRole)
                    ? resolvedRole.ToString()
                    : PlanRole.Viewer.ToString();
                var progress = progressByStableId.TryGetValue(stableId, out var planProgress)
                    ? planProgress
                    : new Sciencetopia.DTOs.UserPlanProgressDto(0.0, Array.Empty<Sciencetopia.DTOs.UserLessonProgressDto>(), 0.0);

                return new
                {
                    id = p.Id,
                    stableId,
                    versionNumber = p.VersionNumber,
                    latestVersionNumber = agg.LatestVersionNumber,
                    currentVersionNumber,
                    isCurrent = p.IsCurrent,
                    status = p.Status,
                    title = p.Title,
                    description = p.Description,
                    updatedAt = p.UpdatedDate,
                    lastStudiedAt = p.LastStudiedAt,
                    createdAt = p.CreatedDate,
                    hasUpgrade,
                    role,
                    progress = progress.planProgress,
                    advancedProgress = progress.advancedTopicProgress
                };
            });

            if (requiresProgressShaping)
            {
                items = sort switch
                {
                    "progressAsc" => items.OrderBy(x => x.progress).ThenByDescending(x => x.lastStudiedAt ?? x.updatedAt),
                    "progressDesc" => items.OrderByDescending(x => x.progress).ThenByDescending(x => x.lastStudiedAt ?? x.updatedAt),
                    _ => items
                };
            }

            var filteredItems = items
                .Where(x => MatchesProgressFilter(x.progress, normalizedProgressStatus))
                .ToList();

            if (requiresProgressShaping)
            {
                total = filteredItems.Count;
                filteredItems = filteredItems
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
            }

            var payload = new { total, page, pageSize, items = filteredItems };
            _cache.Set(responseCacheKey, payload, StudyPlansResponseCacheTtl);

            totalSw.Stop();
            _logger.LogInformation(
                "StudyPlans.List completed. userId={UserId} subjectUserId={SubjectUserId} scope={Scope} visibleStableIds={VisibleCount} page={Page} pageSize={PageSize} total={Total} visibilityMs={VisibilityMs} countMs={CountMs} sliceMs={SliceMs} aggregatesMs={AggregatesMs} progressMs={ProgressMs} permMs={PermMs} totalMs={TotalMs}",
                userId,
                subjectUserId,
                normalizedScope,
                stableSet.Count,
                page,
                pageSize,
                total,
                visibilitySw.ElapsedMilliseconds,
                countSw.ElapsedMilliseconds,
                sliceSw.ElapsedMilliseconds,
                aggregatesSw.ElapsedMilliseconds,
                progressSw.ElapsedMilliseconds,
                permSw.ElapsedMilliseconds,
                totalSw.ElapsedMilliseconds);

            return Ok(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "StudyPlans.List failed; returning minimal fallback. userId={UserId} subjectUserId={SubjectUserId} scope={Scope} page={Page} pageSize={PageSize}",
                    userId,
                    subjectUserId,
                    normalizedScope,
                    page,
                    pageSize);

                var fallbackPayload = await BuildListFallbackAsync(subjectUserId, page, pageSize, q, sort, progressStatus);
                return Ok(fallbackPayload);
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (!await _perm.CanReadAsync(userId, id)) return Forbid();

            var plan = await _db.StudyPlans.FirstOrDefaultAsync(p => p.Id == id);
            if (plan == null) return NotFound();

            var stableId = plan.StableId == Guid.Empty ? plan.Id : plan.StableId;

            var current = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.StableId == stableId)
                .OrderByDescending(p => p.IsCurrent)
                .ThenByDescending(p => p.VersionNumber)
                .Select(p => new { p.Id, p.VersionNumber, p.IsCurrent })
                .FirstOrDefaultAsync();

            var latestVersionNumber = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.StableId == stableId)
                .MaxAsync(p => (int?)p.VersionNumber) ?? plan.VersionNumber;

            return Ok(new
            {
                id = plan.Id,
                stableId,
                versionNumber = plan.VersionNumber,
                isCurrent = plan.IsCurrent,
                currentVersionId = current?.Id,
                currentVersionNumber = current?.VersionNumber,
                latestVersionNumber,
                title = plan.Title,
                description = plan.Description
            });
        }

        // B4-2: GET /StudyPlans/{id}/enrollment/me
        [HttpGet("{id}/Enrollment/Me")]
        public async Task<IActionResult> GetEnrollmentMe([FromRoute] Guid id, [FromServices] Sciencetopia.Services.Cohorts.ICohortService cohortService)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (!await _perm.CanReadAsync(userId, id)) return Forbid();

            var dto = await cohortService.GetEnrollmentForUserAsync(id, userId, HttpContext.RequestAborted);
            return Ok(dto);
        }

        // B4-5: GET /StudyPlans/{id}/joinable-cohorts
        [HttpGet("{id}/JoinableCohorts")]
        public async Task<IActionResult> GetJoinableCohorts([FromRoute] Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (!await _perm.CanReadAsync(userId, id)) return Forbid();

            var stableId = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
                .FirstOrDefaultAsync();
            if (stableId == Guid.Empty) return NotFound();

            var cohorts = await _db.Cohorts.AsNoTracking()
                .Where(c => c.Status == "Active")
                .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                    c => c.StudyGroupStudyPlanId,
                    sgsp => sgsp.Id,
                    (co, sgsp) => new
                    {
                        co.Id,
                        co.Title,
                        StudyGroupId = (Guid?)sgsp.StudyGroupId,
                        co.Visibility,
                        sgsp.StudyPlanStableId
                    })
                .Where(x => x.StudyPlanStableId == stableId)
                .Select(x => new { x.Id, x.Title, x.StudyGroupId, x.Visibility })
                .ToListAsync();

            var alreadyActive = await HasActivePlanEnrollmentByStableIdAsync(userId, stableId);

            var groupScoped = new List<object>();
            var pub = new List<object>();

            foreach (var c in cohorts)
            {
                var reason = alreadyActive ? "alreadyInPlan" : null;
                var canJoin = !alreadyActive;
                if (c.StudyGroupId.HasValue && c.StudyGroupId.Value != Guid.Empty)
                {
                    var isMember = await IsGroupMemberAsync(userId, c.StudyGroupId.Value);
                    if (!isMember) { canJoin = false; reason = reason ?? "notGroupMember"; }
                    groupScoped.Add(new { id = c.Id, title = c.Title, canJoin, reason });
                }
                else if (string.Equals(c.Visibility, "public", StringComparison.OrdinalIgnoreCase))
                {
                    pub.Add(new { id = c.Id, title = c.Title, canJoin, reason });
                }
            }

            return Ok(new { groupScoped, @public = pub, soloAvailable = !alreadyActive });
        }

        [HttpGet("{id}/Permissions/Effective")]
        public async Task<IActionResult> GetEffectivePermission([FromRoute] Guid id, [FromQuery] Guid? cohortId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            var permissions = await _perm.GetEffectivePermissionsAsync(userId, id, cohortId, HttpContext.RequestAborted);
            return Ok(permissions);
        }

        [HttpPost("{id}/SharingSettings")]
        public async Task<IActionResult> SetSharingSettings([FromRoute] Guid id, [FromBody] SetCohortSharingRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (!await _perm.CanEditAsync(userId, id, HttpContext.RequestAborted)) return Forbid();

            var stableId = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.Id == id || p.StableId == id)
                .Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
                .FirstOrDefaultAsync(HttpContext.RequestAborted);
            if (stableId == Guid.Empty) return NotFound();

            var versions = await _db.StudyPlans
                .Where(p => p.StableId == stableId || (p.StableId == Guid.Empty && p.Id == stableId))
                .ToListAsync(HttpContext.RequestAborted);
            if (versions.Count == 0) return NotFound();

            foreach (var version in versions)
            {
                version.MetadataJson = SetAllowCohortSharing(version.MetadataJson, request.AllowCohortSharing);
                version.UpdatedDate = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(HttpContext.RequestAborted);
            await _perm.InvalidatePlanMetadataAsync(stableId, HttpContext.RequestAborted);
            return Ok(new { allowCohortSharing = request.AllowCohortSharing });
        }

        

        public class ShareUserRequest { public string? UserId { get; set; } public PlanRole Role { get; set; } }

        [HttpPost("{id}/Share/User")]
        public async Task<IActionResult> ShareToUser([FromRoute] Guid id, [FromBody] ShareUserRequest req)
        {
            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(actorId)) return Unauthorized();
            if (!await _perm.CanShareAsync(actorId, id)) return Forbid();
            if (string.IsNullOrEmpty(req.UserId)) return BadRequest("UserId required");

            var stableId = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
                .FirstOrDefaultAsync();
            if (stableId == Guid.Empty) return NotFound();

            var versionId = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.StableId == stableId || (p.StableId == Guid.Empty && p.Id == stableId))
                .OrderByDescending(p => p.IsCurrent)
                .ThenByDescending(p => p.VersionNumber)
                .Select(p => p.Id)
                .FirstOrDefaultAsync(HttpContext.RequestAborted);
            if (versionId == Guid.Empty) return NotFound();

            await _personalGroups.EnsureEnrollmentAsync(req.UserId!, stableId, versionId, req.Role, HttpContext.RequestAborted);
            await _perm.InvalidateAsync(req.UserId!, id, HttpContext.RequestAborted);
            return Ok();
        }

        [HttpDelete("{id}/Share/User/{userId}")]
        public async Task<IActionResult> UnshareFromUser([FromRoute] Guid id, [FromRoute] string userId)
        {
            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(actorId)) return Unauthorized();
            if (!await _perm.CanShareAsync(actorId, id)) return Forbid();

            var stableId = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
                .FirstOrDefaultAsync();
            if (stableId == Guid.Empty) return NotFound();

            var personalGroupId = await _personalGroups.EnsurePersonalGroupAsync(userId, HttpContext.RequestAborted);
            var existing = await _db.GroupPlanEnrollments.FirstOrDefaultAsync(r =>
                r.StudyPlanStableId == stableId
                && r.GroupId == personalGroupId
                && r.Status == "Active", HttpContext.RequestAborted);
            if (existing != null)
            {
                existing.Status = "Archived";
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync();
                await _perm.InvalidateAsync(userId, id, HttpContext.RequestAborted);
            }
            return Ok();
        }
    }
}
