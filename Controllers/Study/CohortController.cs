using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Services.Progress;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Models;
using Sciencetopia.Services.Cohorts;
using Neo4j.Driver;

namespace Sciencetopia.Controllers.Study;

[ApiController]
[Route("api/Cohorts")] 
public class CohortController : ControllerBase
{
    private readonly IResourceProgressService _svc;
    private readonly ApplicationDbContext _db;
    private readonly ICohortService _cohorts;
    private readonly IDriver _driver;

    public CohortController(IResourceProgressService svc, ApplicationDbContext db, ICohortService cohorts, IDriver driver)
    {
        _svc = svc;
        _db = db;
        _cohorts = cohorts;
        _driver = driver;
    }

    // Back-compat: POST /cohorts/{cohortId}/upgradeVersion
    [HttpPost("{cohortId:guid}/UpgradeVersion")]
    public async Task<IActionResult> UpgradeToCurrent(Guid cohortId)
    {
        // Load cohort and resolve its plan stableId
        var cohort = await _db.Cohorts.FirstOrDefaultAsync(x => x.Id == cohortId);
        if (cohort == null) return NotFound();

        CohortOffering? currentOffering = null;
        if (cohort.CurrentOfferingId.HasValue)
        {
            currentOffering = await _db.CohortOfferings.FirstOrDefaultAsync(o => o.Id == cohort.CurrentOfferingId.Value);
        }
        if (currentOffering == null)
        {
            currentOffering = await _db.CohortOfferings.FirstOrDefaultAsync(o => o.CohortGroupId == cohort.Id && o.Status == "Active");
        }
        if (currentOffering == null) return BadRequest(new { code = "offering_not_found" });

        var adoption = await _db.StudyGroupStudyPlans.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == currentOffering.StudyGroupStudyPlanId);
        if (adoption == null) return BadRequest(new { code = "adoption_not_found" });

        // Check: allow only managers of group-scoped cohort or ignore check for standalone for now
        var current = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.StableId == adoption.StudyPlanStableId || (p.StableId == Guid.Empty && p.Id == adoption.StudyPlanStableId))
            .OrderByDescending(p => p.IsCurrent)
            .ThenByDescending(p => p.VersionNumber)
            .FirstOrDefaultAsync();
        if (current == null) return BadRequest(new { code = "no_versions" });

        if (current.Id != currentOffering.StudyPlanVersionId)
        {
            currentOffering.Status = "Archived";
            currentOffering.EndAt = DateTime.UtcNow;
            var nextOffering = new CohortOffering
            {
                CohortGroupId = cohort.Id,
                StudyGroupStudyPlanId = currentOffering.StudyGroupStudyPlanId,
                StudyPlanVersionId = current.Id,
                Status = "Active",
                StartAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = User?.Identity?.Name
            };
            _db.CohortOfferings.Add(nextOffering);
            cohort.CurrentOfferingId = nextOffering.Id;
            await _db.SaveChangesAsync();

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
WITH c, v AS pv, collect(u) AS users
UNWIND users AS u
MERGE (u)-[:ENROLLED_IN]->(pv)";
                await tx.RunAsync(cypher, new { cohortId = cohortId.ToString(), versionNumber = current.VersionNumber });
            });

            await AlignCohortEnrollmentsAsync(cohortId, current.Id);
        }
        return Ok(new { pinnedVersionNumber = current.VersionNumber });
    }

    [HttpGet("{cohortId:guid}/Stats/Summary")]
    public async Task<IActionResult> Summary(Guid cohortId)
    {
        var dto = await _svc.GetCohortSummaryAsync(cohortId);
        return Ok(dto);
    }

    // B4-3: GET /cohorts/{cohortId}
    [HttpGet("{cohortId:guid}")]
    public async Task<IActionResult> GetCohort(Guid cohortId)
    {
        var c = await _db.Cohorts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cohortId);
        if (c == null) return NotFound();

        CohortOffering? offering = null;
        if (c.CurrentOfferingId.HasValue)
        {
            offering = await _db.CohortOfferings.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == c.CurrentOfferingId.Value);
        }
        if (offering == null)
        {
            offering = await _db.CohortOfferings.AsNoTracking()
                .FirstOrDefaultAsync(o => o.CohortGroupId == c.Id && o.Status == "Active");
        }
        if (offering == null) return BadRequest(new { code = "offering_not_found" });

        var adoption = await _db.StudyGroupStudyPlans.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == offering.StudyGroupStudyPlanId);

        var planInfo = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.Id == offering.StudyPlanVersionId)
            .Select(p => new
            {
                StableId = p.StableId == Guid.Empty ? p.Id : p.StableId,
                p.VersionNumber
            })
            .FirstOrDefaultAsync();

        var memberCount = await _db.UserGroups.AsNoTracking()
            .CountAsync(ug => ug.GroupId == c.Id);

        return Ok(new
        {
            id = c.Id,
            studyPlanId = offering.StudyPlanVersionId,
            studyPlanStableId = adoption?.StudyPlanStableId ?? planInfo?.StableId,
            studyGroupId = c.StudyGroupId,
            enrollMode = c.EnrollmentPolicy.ToString(),
            pinnedVersionNumber = planInfo?.VersionNumber,
            membersCount = memberCount,
            createdAt = c.CreatedAt,
            createdBy = c.CreatedBy,
            title = c.Title,
            visibility = c.Visibility
        });
    }

    // B3-3: AutoEnroll batch trigger
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
        return Ok(list);
    }

    [HttpGet("{cohortId:guid}/Stats/Leaderboard")]
    public async Task<IActionResult> Leaderboard(Guid cohortId, [FromQuery] int top = 20)
    {
        var list = await _svc.GetCohortLeaderboardAsync(cohortId, top);
        return Ok(list);
    }

    private async Task AlignCohortEnrollmentsAsync(Guid cohortId, Guid planVersionId)
    {
        var userIds = await _db.UserGroups.AsNoTracking()
            .Where(x => x.GroupId == cohortId && x.Status == "Active")
            .Select(x => x.UserId)
            .ToListAsync();
        if (userIds.Count == 0)
        {
            return;
        }

        var existing = await _db.StudyPlanEnrollments
            .Where(x => x.ScopeType == "Cohort" && x.ScopeId == cohortId && userIds.Contains(x.UserId))
            .ToListAsync();
        var existingMap = existing.ToDictionary(x => x.UserId, StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        foreach (var userId in userIds)
        {
            if (existingMap.TryGetValue(userId, out var enrollment))
            {
                enrollment.PlanVersionId = planVersionId;
                if (!string.Equals(enrollment.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    enrollment.Status = "Active";
                }
                enrollment.UpdatedAt = now;
            }
            else
            {
                _db.StudyPlanEnrollments.Add(new StudyPlanEnrollment
                {
                    UserId = userId,
                    ScopeType = "Cohort",
                    ScopeId = cohortId,
                    PlanVersionId = planVersionId,
                    Status = "Active",
                    EnrolledAt = now
                });
            }
        }

        await _db.SaveChangesAsync();
    }
}
