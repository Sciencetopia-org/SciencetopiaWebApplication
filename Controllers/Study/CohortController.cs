using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Sciencetopia.Data;
using Sciencetopia.DTOs;
using Sciencetopia.Services.Cohorts;
using Sciencetopia.Services.Progress;
using System.Security.Claims;

namespace Sciencetopia.Controllers.Study;

[ApiController]
[Route("api/Cohorts")]
public class CohortController : ControllerBase
{
    private readonly IResourceProgressService _svc;
    private readonly ApplicationDbContext _db;
    private readonly ICohortService _cohorts;
    private readonly IDriver _driver;
    private readonly UserService _userService;

    public CohortController(IResourceProgressService svc, ApplicationDbContext db, ICohortService cohorts, IDriver driver, UserService userService)
    {
        _svc = svc;
        _db = db;
        _cohorts = cohorts;
        _driver = driver;
        _userService = userService;
    }

    [HttpPost("{cohortId:guid}/UpgradeVersion")]
    public async Task<IActionResult> UpgradeToCurrent(Guid cohortId)
    {
        var row = await _db.Cohorts
            .Join(_db.StudyGroupStudyPlans,
                c => c.StudyGroupStudyPlanId,
                sgsp => sgsp.Id,
                (c, sgsp) => new { Cohort = c, Adoption = sgsp })
            .FirstOrDefaultAsync(x => x.Cohort.Id == cohortId);
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

    [HttpGet("{cohortId:guid}/Stats/Summary")]
    public async Task<IActionResult> Summary(Guid cohortId)
    {
        var dto = await _svc.GetCohortSummaryAsync(cohortId);
        return Ok(dto);
    }

    [HttpGet("{cohortId:guid}")]
    public async Task<IActionResult> GetCohort(Guid cohortId)
    {
        var row = await _db.Cohorts.AsNoTracking()
            .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                c => c.StudyGroupStudyPlanId,
                sgsp => sgsp.Id,
                (c, sgsp) => new { Cohort = c, Adoption = sgsp })
            .Join(_db.StudyPlans.AsNoTracking(),
                x => x.Cohort.StudyPlanVersionId,
                p => p.Id,
                (x, p) => new { x.Cohort, x.Adoption, Version = p })
            .FirstOrDefaultAsync(x => x.Cohort.Id == cohortId);
        if (row == null) return NotFound();

        var memberCount = await _db.UserGroups.AsNoTracking()
            .CountAsync(ug => ug.GroupId == row.Cohort.Id && ug.Status == "Active");

        return Ok(new
        {
            id = row.Cohort.Id,
            studyPlanId = row.Cohort.StudyPlanVersionId,
            studyPlanStableId = row.Adoption.StudyPlanStableId,
            studyGroupId = row.Adoption.StudyGroupId,
            enrollMode = row.Cohort.EnrollmentPolicy.ToString(),
            pinnedVersionNumber = row.Version.VersionNumber,
            membersCount = memberCount,
            createdAt = row.Cohort.CreatedAt,
            createdBy = row.Cohort.CreatedBy,
            title = row.Cohort.Title,
            visibility = row.Cohort.Visibility
        });
    }

    [HttpPost("{cohortId:guid}/AutoEnroll/{groupId:guid}/Run")]
    public async Task<IActionResult> RunAutoEnroll(Guid cohortId, Guid groupId)
    {
        var processed = await _cohorts.RunAutoEnrollAsync(cohortId, groupId, HttpContext.RequestAborted);
        return Ok(new { processed });
    }

    [HttpGet("{cohortId:guid}/Stats/Lessons")]
    public async Task<IActionResult> LessonStats(Guid cohortId)
    {
        var list = await _svc.GetCohortLessonStatsAsync(cohortId);
        return Ok(await EnrichLessonStatsAsync(list));
    }

    [HttpGet("{cohortId:guid}/Stats/Leaderboard")]
    public async Task<IActionResult> Leaderboard(Guid cohortId, [FromQuery] int top = 20)
    {
        var list = await _svc.GetCohortLeaderboardAsync(cohortId, top);
        return Ok(await EnrichLeaderboardAsync(list));
    }

    [HttpGet("{cohortId:guid}/Stats/Dashboard")]
    public async Task<IActionResult> Dashboard(Guid cohortId, [FromQuery] int top = 20)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var row = await _db.Cohorts.AsNoTracking()
            .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                c => c.StudyGroupStudyPlanId,
                sgsp => sgsp.Id,
                (c, sgsp) => new { Cohort = c, Adoption = sgsp })
            .Join(_db.StudyPlans.AsNoTracking(),
                x => x.Cohort.StudyPlanVersionId,
                p => p.Id,
                (x, p) => new { x.Cohort, x.Adoption, Version = p })
            .FirstOrDefaultAsync(x => x.Cohort.Id == cohortId);
        if (row == null) return NotFound();

        var isGroupMember = await _db.UserGroups.AsNoTracking()
            .AnyAsync(x => x.GroupId == row.Adoption.StudyGroupId
                && x.UserId == userId
                && (string.IsNullOrEmpty(x.Status) || x.Status == "Active" || x.Status == "active"));
        if (!isGroupMember) return Forbid();

        var isEnrolled = await _db.UserGroups.AsNoTracking()
            .AnyAsync(x => x.UserId == userId
                && x.GroupId == cohortId
                && (string.IsNullOrEmpty(x.Status) || x.Status == "Active" || x.Status == "active"));

        if (row.Cohort.EnrollmentPolicy == Sciencetopia.Models.Enums.CohortEnrollMode.Auto)
        {
            await _cohorts.RunAutoEnrollAsync(cohortId, row.Adoption.StudyGroupId, HttpContext.RequestAborted);
            isEnrolled = await _db.UserGroups.AsNoTracking()
                .AnyAsync(x => x.UserId == userId
                    && x.GroupId == cohortId
                    && (string.IsNullOrEmpty(x.Status) || x.Status == "Active" || x.Status == "active"));
        }
        else if (!isEnrolled && string.Equals(row.Cohort.CreatedBy, userId, StringComparison.OrdinalIgnoreCase))
        {
            await _cohorts.JoinCohortAsync(cohortId, userId, true, HttpContext.RequestAborted);
            isEnrolled = true;
        }

        var stableId = row.Adoption.StudyPlanStableId;
        var summaryTask = _svc.GetCohortSummaryAsync(cohortId);
        var lessonsTask = _svc.GetCohortLessonStatsAsync(cohortId);
        var leaderboardTask = _svc.GetCohortLeaderboardAsync(cohortId, Math.Max(top, 1000));
        var myProgressTask = _svc.GetPlanProgressAsync(userId, stableId);

        await Task.WhenAll(summaryTask, lessonsTask, leaderboardTask, myProgressTask);

        var allLeaderboard = (await EnrichLeaderboardAsync(leaderboardTask.Result))
            .OrderByDescending(x => x.progress)
            .ToList();
        var lessons = await EnrichLessonStatsAsync(lessonsTask.Result);
        var myRank = allLeaderboard.FindIndex(x => string.Equals(x.userId, userId, StringComparison.OrdinalIgnoreCase));
        var ranked = allLeaderboard
            .Take(Math.Clamp(top, 1, 100))
            .Select((x, idx) => new RankedLeaderboardItemDto(
                x.userId,
                x.displayName,
                x.progress,
                idx + 1,
                string.Equals(x.userId, userId, StringComparison.OrdinalIgnoreCase),
                x.avatarUrl))
            .ToList();

        var dashboard = new CohortDashboardDto(
            new CohortDashboardCohortDto(
                row.Cohort.Id,
                row.Cohort.Title,
                row.Adoption.StudyGroupId,
                row.Cohort.StudyPlanVersionId,
                stableId,
                row.Version.VersionNumber,
                row.Cohort.EnrollmentPolicy.ToString(),
                row.Cohort.Visibility),
            summaryTask.Result,
            new CohortDashboardMeDto(
                isEnrolled,
                myProgressTask.Result.planProgress,
                myRank >= 0 ? myRank + 1 : null,
                true),
            ranked,
            lessons);

        return Ok(dashboard);
    }

    private async Task<List<LessonStatDto>> EnrichLessonStatsAsync(IEnumerable<LessonStatDto> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0) return list;

        var missingTitleIds = list
            .Where(x => IsMissingLessonTitle(x))
            .Select(x => x.lessonId)
            .Distinct()
            .ToList();
        if (missingTitleIds.Count == 0) return list;

        var titles = await _db.Lessons.AsNoTracking()
            .Where(x => missingTitleIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title);

        return list
            .Select(x => IsMissingLessonTitle(x) && titles.TryGetValue(x.lessonId, out var title)
                ? x with { lessonTitle = title }
                : x)
            .ToList();
    }

    private static bool IsMissingLessonTitle(LessonStatDto item)
    {
        var title = item.lessonTitle?.Trim();
        return string.IsNullOrWhiteSpace(title)
            || string.Equals(title, item.lessonId.ToString(), StringComparison.OrdinalIgnoreCase)
            || Guid.TryParse(title, out _);
    }

    private async Task<List<LeaderboardItemDto>> EnrichLeaderboardAsync(IEnumerable<LeaderboardItemDto> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0) return list;

        var displayInfo = await _userService.GetUserDisplayInfoByIdsAsync(list.Select(x => x.userId));
        return list.Select(item =>
        {
            displayInfo.TryGetValue(item.userId, out var info);
            var displayName = !string.IsNullOrWhiteSpace(info?.UserName)
                ? info.UserName
                : (!string.IsNullOrWhiteSpace(item.displayName) ? item.displayName : item.userId);
            return item with
            {
                displayName = displayName,
                avatarUrl = info?.AvatarUrl ?? item.avatarUrl
            };
        }).ToList();
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
