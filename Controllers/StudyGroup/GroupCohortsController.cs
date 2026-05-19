using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using Sciencetopia.Data;
using Sciencetopia.Models;
using Sciencetopia.Models.Enums;
using Sciencetopia.Services;
using Sciencetopia.Services.Cohorts;
using Sciencetopia.Services.Progress;
using System.Security.Claims;
using System.Text.Json.Serialization;

namespace Sciencetopia.Controllers.StudyGroups;

[ApiController]
[Route("api/Groups/{groupId:guid}")]
public class GroupCohortsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IDriver _driver;
    private readonly StudyGroupService _groups;
    private readonly IResourceProgressService _progress;
    private readonly PermissionService _perm;
    private readonly ICohortService _cohorts;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GroupCohortsController> _logger;

    public GroupCohortsController(
        ApplicationDbContext db,
        IDriver driver,
        StudyGroupService groups,
        IResourceProgressService progress,
        PermissionService perm,
        ICohortService cohorts,
        IMemoryCache cache,
        ILogger<GroupCohortsController> logger)
    {
        _db = db;
        _driver = driver;
        _groups = groups;
        _progress = progress;
        _perm = perm;
        _cohorts = cohorts;
        _cache = cache;
        _logger = logger;
    }

    public class CreateGroupCohortRequest
    {
        public string? Title { get; set; }
        public string? Visibility { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CohortEnrollMode? EnrollMode { get; set; }
    }

    public class UpdateGroupCohortRequest
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public CohortEnrollMode? EnrollMode { get; set; }
        public int? PinnedVersionNumber { get; set; }
    }

    private sealed record GroupCohortPlanCohortCard(
        Guid Id,
        Guid StudyPlanId,
        Guid StudyPlanStableId,
        string? Title,
        string? Visibility,
        string EnrollMode,
        int? PinnedVersionNumber,
        int MemberCount,
        double AvgProgress,
        DateTime CreatedAt,
        string? CreatedBy);

    [HttpGet("CohortPlans")]
    public async Task<IActionResult> List(Guid groupId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var cacheKey = $"group:cohortplans:{groupId}:{userId}";
        if (_cache.TryGetValue<object>(cacheKey, out var cached) && cached != null) return Ok(cached);

        var adoptions = await _db.StudyGroupStudyPlans.AsNoTracking()
            .Where(x => x.StudyGroupId == groupId)
            .OrderByDescending(x => x.UpdatedDate)
            .ToListAsync();

        var adoptionIds = adoptions.Select(x => x.Id).ToList();
        var stableIds = adoptions.Select(x => x.StudyPlanStableId).Where(x => x != Guid.Empty).Distinct().ToList();

        var planSummaries = await _db.StudyPlans.AsNoTracking()
            .Where(p => stableIds.Contains(p.StableId == Guid.Empty ? p.Id : p.StableId))
            .Select(p => new { p.Id, p.StableId, p.VersionNumber, p.IsCurrent, p.Title })
            .ToListAsync();

        var currentVersionLookup = planSummaries
            .GroupBy(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.IsCurrent).ThenByDescending(x => x.VersionNumber).First().VersionNumber);

        var titleLookup = planSummaries
            .GroupBy(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.VersionNumber).First().Title);

        var versionByStable = planSummaries
            .GroupBy(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.IsCurrent).ThenByDescending(x => x.VersionNumber).First());

        var cohorts = await _db.Cohorts.AsNoTracking()
            .Where(c => adoptionIds.Contains(c.StudyGroupStudyPlanId) && c.Status == "Active")
            .ToListAsync();

        var allVersionIds = adoptions
            .Where(x => x.PlanVersionId.HasValue)
            .Select(x => x.PlanVersionId!.Value)
            .Concat(cohorts.Select(c => c.StudyPlanVersionId))
            .Distinct()
            .ToList();

        var versionLookup = await _db.StudyPlans.AsNoTracking()
            .Where(p => allVersionIds.Contains(p.Id))
            .Select(p => new { p.Id, p.StableId, p.VersionNumber, p.IsCurrent, p.Title })
            .ToDictionaryAsync(x => x.Id, x => x);

        var cohortsByAdoption = cohorts
            .GroupBy(x => x.StudyGroupStudyPlanId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).ToList());

        var cohortIds = cohorts.Select(c => c.Id).Distinct().ToList();
        var memberCounts = await GetCohortMemberCountsAsync(cohortIds);
        var uniqueMemberCountsByAdoption = await GetUniqueCohortMemberCountsByAdoptionAsync(cohorts);
        var summaryTasks = cohortIds.ToDictionary(id => id, id => _progress.GetCohortSummaryAsync(id));
        await Task.WhenAll(summaryTasks.Values);

        var result = await Task.WhenAll(adoptions.Select(async adoption =>
        {
            var stableId = adoption.StudyPlanStableId;
            var targetVersionId = adoption.PlanVersionId;
            if (!targetVersionId.HasValue && versionByStable.TryGetValue(stableId, out var resolvedVersion))
            {
                targetVersionId = resolvedVersion.Id;
            }

            var title = stableId != Guid.Empty && titleLookup.TryGetValue(stableId, out var t) ? t : null;
            var currentNo = stableId != Guid.Empty && currentVersionLookup.TryGetValue(stableId, out var cur) ? cur : (int?)null;
            var pinnedNo = adoption.PinnedVersionNumber;
            if (!pinnedNo.HasValue && targetVersionId.HasValue && versionLookup.TryGetValue(targetVersionId.Value, out var adoptionVersion))
            {
                pinnedNo = adoptionVersion.VersionNumber;
                title ??= adoptionVersion.Title;
            }

            var cohortCards = new List<GroupCohortPlanCohortCard>();
            if (cohortsByAdoption.TryGetValue(adoption.Id, out var cohortRows))
            {
                cohortCards = cohortRows.Select(c =>
                {
                    versionLookup.TryGetValue(c.StudyPlanVersionId, out var versionInfo);
                    var memberCount = memberCounts.TryGetValue(c.Id, out var cnt) ? cnt : 0;
                    var summary = summaryTasks.TryGetValue(c.Id, out var task) ? task.Result : default;
                    return new GroupCohortPlanCohortCard(
                        c.Id,
                        c.StudyPlanVersionId,
                        stableId,
                        c.Title,
                        c.Visibility,
                        c.EnrollmentPolicy.ToString(),
                        versionInfo?.VersionNumber,
                        summary.memberCount > 0 ? summary.memberCount : memberCount,
                        summary.avgProgress,
                        c.CreatedAt,
                        c.CreatedBy);
                }).ToList();
            }

            var role = targetVersionId.HasValue
                ? await _perm.GetEffectivePlanRoleAsync(userId, targetVersionId.Value)
                : PlanRole.Viewer;
            var primaryCohort = cohortCards.FirstOrDefault();
            uniqueMemberCountsByAdoption.TryGetValue(adoption.Id, out var uniqueMemberCount);

            return new
            {
                id = primaryCohort?.Id,
                studyPlanId = targetVersionId,
                studyPlanStableId = stableId,
                planTitle = title,
                planCurrentVersionNumber = currentNo,
                title = primaryCohort?.Title,
                visibility = primaryCohort?.Visibility,
                enrollMode = primaryCohort?.EnrollMode,
                pinnedVersionNumber = pinnedNo,
                memberCount = uniqueMemberCount,
                avgProgress = cohortCards.Count > 0 ? cohortCards.Average(x => x.AvgProgress) : 0,
                role = role.ToString(),
                sharePermission = adoption.Permission,
                autoEnroll = adoption.AutoEnroll,
                createdAt = adoption.CreatedDate,
                createdBy = adoption.CreatedBy,
                cohorts = cohortCards
            };
        }));

        _cache.Set(cacheKey, result, TimeSpan.FromSeconds(30));
        return Ok(result);
    }

    [HttpGet("Plans/{planStableId:guid}/Cohorts")]
    public async Task<IActionResult> ListByPlan(Guid groupId, Guid planStableId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _groups.IsUserMemberAsync(groupId.ToString(), userId)) return Forbid();

        var rows = await _db.Cohorts.AsNoTracking()
            .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                c => c.StudyGroupStudyPlanId,
                sgsp => sgsp.Id,
                (c, sgsp) => new { Cohort = c, Adoption = sgsp })
            .Where(x => x.Adoption.StudyGroupId == groupId
                && x.Adoption.StudyPlanStableId == planStableId
                && x.Cohort.Status == "Active")
            .OrderByDescending(x => x.Cohort.CreatedAt)
            .ToListAsync();

        var versionIds = rows.Select(x => x.Cohort.StudyPlanVersionId).Distinct().ToList();
        var versionMap = await _db.StudyPlans.AsNoTracking()
            .Where(p => versionIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Title, p.VersionNumber })
            .ToDictionaryAsync(x => x.Id, x => x);

        var memberCounts = await GetCohortMemberCountsAsync(rows.Select(x => x.Cohort.Id).ToList());

        var result = rows.Select(x =>
        {
            versionMap.TryGetValue(x.Cohort.StudyPlanVersionId, out var versionInfo);
            memberCounts.TryGetValue(x.Cohort.Id, out var memberCount);
            return new
            {
                id = x.Cohort.Id,
                studyGroupId = x.Adoption.StudyGroupId,
                studyPlanStableId = x.Adoption.StudyPlanStableId,
                studyPlanId = x.Cohort.StudyPlanVersionId,
                planTitle = versionInfo?.Title,
                title = x.Cohort.Title,
                visibility = x.Cohort.Visibility,
                enrollMode = x.Cohort.EnrollmentPolicy.ToString(),
                pinnedVersionNumber = versionInfo?.VersionNumber,
                memberCount,
                createdAt = x.Cohort.CreatedAt,
                createdBy = x.Cohort.CreatedBy
            };
        });

        return Ok(result);
    }

    [HttpPost("Plans/{planStableId:guid}/Cohorts")]
    public async Task<IActionResult> Create(Guid groupId, Guid planStableId, [FromBody] CreateGroupCohortRequest body)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _groups.IsUserManagerAsync(groupId.ToString(), userId)) return Forbid();

        var adoption = await _db.StudyGroupStudyPlans.AsNoTracking()
            .FirstOrDefaultAsync(x => x.StudyGroupId == groupId && x.StudyPlanStableId == planStableId);
        if (adoption == null) return NotFound(new { message = "Study plan not adopted by this group." });

        var current = adoption.PlanVersionId.HasValue
            ? await _db.StudyPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == adoption.PlanVersionId.Value)
            : null;
        current ??= await _db.StudyPlans.AsNoTracking()
            .Where(p => p.StableId == planStableId || (p.StableId == Guid.Empty && p.Id == planStableId))
            .OrderByDescending(p => p.IsCurrent)
            .ThenByDescending(p => p.VersionNumber)
            .FirstOrDefaultAsync();
        if (current == null) return NotFound(new { message = "Study plan version not found." });

        var cohortId = Guid.NewGuid();
        var title = body.Title;
        var visibility = string.IsNullOrEmpty(body.Visibility) ? "group" : body.Visibility;
        var enrollmentPolicy = body.EnrollMode ?? CohortEnrollMode.OptIn;

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                _db.Groups.Add(new GroupEntity
                {
                    Id = cohortId,
                    Kind = "Cohort",
                    CreatedByUserId = userId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                });

                _db.Cohorts.Add(new CohortEntity
                {
                    Id = cohortId,
                    StudyGroupStudyPlanId = adoption.Id,
                    StudyPlanVersionId = current.Id,
                    Title = title,
                    Visibility = visibility,
                    EnrollmentPolicy = enrollmentPolicy,
                    Status = "Active",
                    StartAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = userId
                });

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                _db.ChangeTracker.Clear();
                throw;
            }
        });

        var graphSynced = await SyncCohortGraphAsync(cohortId, groupId, planStableId, current.VersionNumber, title, visibility);

        var enrolledCount = 0;
        if (enrollmentPolicy == CohortEnrollMode.Auto || adoption.AutoEnroll)
        {
            enrolledCount = await _cohorts.RunAutoEnrollAsync(cohortId, groupId, HttpContext.RequestAborted);
        }
        else
        {
            await _cohorts.JoinCohortAsync(cohortId, userId, HttpContext.RequestAborted);
            enrolledCount = 1;
        }

        return Ok(new { cohortId, pinnedVersionNumber = current.VersionNumber, graphSynced, enrolledCount });
    }

    [HttpPatch("Cohorts/{cohortId:guid}")]
    [Authorize(Policy = "Cohort.Manage")]
    public async Task<IActionResult> Patch(Guid groupId, Guid cohortId, [FromBody] UpdateGroupCohortRequest body)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _groups.IsUserManagerAsync(groupId.ToString(), userId)) return Forbid();

        var row = await _db.Cohorts
            .Join(_db.StudyGroupStudyPlans,
                c => c.StudyGroupStudyPlanId,
                sgsp => sgsp.Id,
                (c, sgsp) => new { Cohort = c, Adoption = sgsp })
            .FirstOrDefaultAsync(x => x.Cohort.Id == cohortId && x.Adoption.StudyGroupId == groupId);
        if (row == null) return NotFound();

        if (body.EnrollMode.HasValue) row.Cohort.EnrollmentPolicy = body.EnrollMode.Value;

        int? pinnedNo = null;
        if (body.PinnedVersionNumber.HasValue)
        {
            var requestedNo = body.PinnedVersionNumber.Value;
            var planVersion = await _db.StudyPlans.AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    (p.StableId == row.Adoption.StudyPlanStableId || (p.StableId == Guid.Empty && p.Id == row.Adoption.StudyPlanStableId))
                    && p.VersionNumber == requestedNo);
            if (planVersion == null) return BadRequest(new { code = "version_not_found", versionNumber = requestedNo });

            row.Cohort.StudyPlanVersionId = planVersion.Id;
            pinnedNo = requestedNo;
        }

        await _db.SaveChangesAsync();

        if (pinnedNo.HasValue)
        {
            await AlignCohortGraphVersionAsync(cohortId, pinnedNo.Value);
        }

        return Ok();
    }

    [HttpPost("Cohorts/{cohortId:guid}/UpgradeVersion")]
    [Authorize(Policy = "Cohort.Manage")]
    public async Task<IActionResult> UpgradeToCurrent(Guid groupId, Guid cohortId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _groups.IsUserManagerAsync(groupId.ToString(), userId)) return Forbid();

        var row = await _db.Cohorts
            .Join(_db.StudyGroupStudyPlans,
                c => c.StudyGroupStudyPlanId,
                sgsp => sgsp.Id,
                (c, sgsp) => new { Cohort = c, Adoption = sgsp })
            .FirstOrDefaultAsync(x => x.Cohort.Id == cohortId && x.Adoption.StudyGroupId == groupId);
        if (row == null) return NotFound();

        var current = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.StableId == row.Adoption.StudyPlanStableId || (p.StableId == Guid.Empty && p.Id == row.Adoption.StudyPlanStableId))
            .OrderByDescending(p => p.IsCurrent)
            .ThenByDescending(p => p.VersionNumber)
            .FirstOrDefaultAsync();
        if (current == null) return BadRequest(new { code = "no_versions" });

        if (row.Cohort.StudyPlanVersionId != current.Id)
        {
            row.Cohort.StudyPlanVersionId = current.Id;
            await _db.SaveChangesAsync();
            await AlignCohortGraphVersionAsync(cohortId, current.VersionNumber);
        }

        return Ok(new { pinnedVersionNumber = current.VersionNumber });
    }

    private async Task<Dictionary<Guid, int>> GetCohortMemberCountsAsync(List<Guid> cohortIds)
    {
        if (cohortIds.Count == 0) return new Dictionary<Guid, int>();
        return await _db.UserGroups.AsNoTracking()
            .Where(ug => cohortIds.Contains(ug.GroupId) && (string.IsNullOrEmpty(ug.Status) || ug.Status == "Active" || ug.Status == "active"))
            .GroupBy(ug => ug.GroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Select(x => x.UserId).Distinct().Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count);
    }

    private async Task<Dictionary<Guid, int>> GetUniqueCohortMemberCountsByAdoptionAsync(List<CohortEntity> cohorts)
    {
        if (cohorts.Count == 0) return new Dictionary<Guid, int>();

        var cohortToAdoption = cohorts
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First().StudyGroupStudyPlanId);
        var cohortIds = cohortToAdoption.Keys.ToList();

        var rows = await _db.UserGroups.AsNoTracking()
            .Where(ug => cohortIds.Contains(ug.GroupId)
                && !string.IsNullOrEmpty(ug.UserId)
                && (string.IsNullOrEmpty(ug.Status) || ug.Status == "Active" || ug.Status == "active"))
            .Select(ug => new { CohortId = ug.GroupId, ug.UserId })
            .ToListAsync();

        return rows
            .Where(x => cohortToAdoption.ContainsKey(x.CohortId))
            .GroupBy(x => cohortToAdoption[x.CohortId])
            .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).Distinct().Count());
    }

    private async Task<bool> SyncCohortGraphAsync(Guid cohortId, Guid groupId, Guid planStableId, int versionNumber, string? title, string? visibility)
    {
        try
        {
            await using var session = _driver.AsyncSession();
            await session.ExecuteWriteAsync(async tx =>
            {
                var cypher = @"
MATCH (p:StudyPlan {id:$planId})
MERGE (c:Cohort {id:$cohortId})
SET c.title=$title, c.visibility=$visibility
MERGE (c)-[:FOR_PLAN]->(p)
MERGE (g:StudyGroup {id:$groupId})
MERGE (c)-[:SCOPED_BY]->(g)
MERGE (v:PlanVersion {studyPlanId:$planId, versionNumber:$versionNumber})
MERGE (c)-[:OF_VERSION]->(v)";
                await tx.RunAsync(cypher, new
                {
                    planId = planStableId.ToString(),
                    cohortId = cohortId.ToString(),
                    title,
                    visibility,
                    groupId = groupId.ToString(),
                    versionNumber
                });
            });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Created or updated cohort in SQL but failed to sync Neo4j. groupId={GroupId} planStableId={PlanStableId} cohortId={CohortId}", groupId, planStableId, cohortId);
            return false;
        }
    }

    private async Task AlignCohortGraphVersionAsync(Guid cohortId, int versionNumber)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (c:Cohort {id:$cohortId})
OPTIONAL MATCH (c)-[r:OF_VERSION]->(:PlanVersion)
DELETE r
WITH c
MATCH (p:StudyPlan)<-[:FOR_PLAN]-(c)
MERGE (v:PlanVersion {studyPlanId:p.id, versionNumber:$versionNumber})
MERGE (c)-[:OF_VERSION]->(v)
OPTIONAL MATCH (u:User)-[:IN_COHORT]->(c)
OPTIONAL MATCH (u)-[:MEMBER_OF]->(pg:Group {kind:'PersonalGroup'})
WITH c, v AS pv, collect(pg) AS personalGroups
UNWIND personalGroups AS pg
WITH pg, pv WHERE pg IS NOT NULL
MERGE (pg)-[:ENROLLED_IN]->(pv)";
            await tx.RunAsync(cypher, new { cohortId = cohortId.ToString(), versionNumber });
        });
    }
}
