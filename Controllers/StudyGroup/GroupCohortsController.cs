using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Sciencetopia.Data;
using Sciencetopia.Models;
using Sciencetopia.Models.Enums;
using Sciencetopia.Services;
using Microsoft.AspNetCore.Authorization;

namespace Sciencetopia.Controllers.StudyGroups;

[ApiController]
[Route("api/Groups/{groupId:guid}")]
public class GroupCohortsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IDriver _driver;
    private readonly StudyGroupService _groups;

    public GroupCohortsController(ApplicationDbContext db, IDriver driver, StudyGroupService groups)
    {
        _db = db;
        _driver = driver;
        _groups = groups;
    }

    public class CreateGroupCohortRequest
    {
        public string? Title { get; set; }
        public string? Visibility { get; set; }
        public CohortEnrollMode? EnrollMode { get; set; }
    }

    [HttpGet("CohortPlans")] // B5-4 list group-scoped
    public async Task<IActionResult> List(Guid groupId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var cohorts = await _db.Cohorts.AsNoTracking()
            .Where(c => c.StudyGroupId == groupId)
            .ToListAsync();

        var stableIds = cohorts.Select(c => c.StudyPlanStableId).Distinct().ToList();
        var planSummaries = await _db.StudyPlans.AsNoTracking()
            .Where(p => stableIds.Contains(p.StableId))
            .Select(p => new { p.StableId, p.VersionNumber, p.IsCurrent, p.Title })
            .ToListAsync();

        var currentVersionLookup = planSummaries
            .GroupBy(p => p.StableId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.IsCurrent).ThenByDescending(x => x.VersionNumber).First().VersionNumber
            );

        var titleLookup = planSummaries
            .GroupBy(p => p.StableId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.VersionNumber).First().Title
            );

        var result = cohorts.Select(c => new
        {
            c.Id,
            StudyPlanStableId = c.StudyPlanStableId,
            PlanTitle = titleLookup.TryGetValue(c.StudyPlanStableId, out var title) ? title : null,
            PlanCurrentVersionNumber = currentVersionLookup.TryGetValue(c.StudyPlanStableId, out var current) ? current : (int?)null,
            c.Title,
            c.Visibility,
            c.EnrollMode,
            c.PinnedVersionNumber,
            c.MembersCount,
            c.CreatedAt,
            c.CreatedBy
        });

        return Ok(result);
    }

    [HttpPost("Plans/{planStableId:guid}/Cohorts")] // B5-4 create group-scoped
    [Authorize(Policy = "Plan.Edit")]
    public async Task<IActionResult> Create(Guid groupId, Guid planStableId, [FromBody] CreateGroupCohortRequest body)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var isManager = await _groups.IsUserManagerAsync(groupId.ToString(), userId);
        if (!isManager) return Forbid();

        var planVersions = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.StableId == planStableId)
            .OrderByDescending(p => p.VersionNumber)
            .ToListAsync();

        if (planVersions.Count == 0)
        {
            return NotFound(new { message = "Study plan not found." });
        }

        var current = planVersions.FirstOrDefault(p => p.IsCurrent) ?? planVersions.First();
        var pinnedNo = current.VersionNumber;

        var cohort = new StudyPlanCohort
        {
            Id = Guid.NewGuid(),
            StudyPlanStableId = planStableId,
            StudyGroupId = groupId,
            Title = body.Title,
            Visibility = string.IsNullOrEmpty(body.Visibility) ? "group" : body.Visibility,
            EnrollMode = body.EnrollMode ?? CohortEnrollMode.OptIn,
            PinnedVersionNumber = pinnedNo,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };

        _db.Cohorts.Add(cohort);
        await _db.SaveChangesAsync();

        // Neo4j wiring
        await using (var session = _driver.AsyncSession())
        {
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
MERGE (c)-[:OF_VERSION]->(v)
";
                await tx.RunAsync(cypher, new
                {
                    planId = planStableId.ToString(),
                    cohortId = cohort.Id.ToString(),
                    title = cohort.Title,
                    visibility = cohort.Visibility,
                    groupId = groupId.ToString(),
                    versionNumber = pinnedNo
                });
            });
        }

        return Ok(new { cohortId = cohort.Id, pinnedVersionNumber = pinnedNo });
    }

    public class UpdateGroupCohortRequest
    {
        public CohortEnrollMode? EnrollMode { get; set; }
        public int? PinnedVersionNumber { get; set; }
    }

    [HttpPatch("Cohorts/{cohortId:guid}")] // B5-4 patch enrollMode/pinnedVersion
    [Authorize(Policy = "Cohort.Manage")]
    public async Task<IActionResult> Patch(Guid groupId, Guid cohortId, [FromBody] UpdateGroupCohortRequest body)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var isManager = await _groups.IsUserManagerAsync(groupId.ToString(), userId);
        if (!isManager) return Forbid();

        var cohort = await _db.Cohorts.FirstOrDefaultAsync(x => x.Id == cohortId && x.StudyGroupId == groupId);
        if (cohort == null) return NotFound();

        if (body.EnrollMode.HasValue)
        {
            cohort.EnrollMode = body.EnrollMode.Value;
        }

        int? pinnedNo = null;
        if (body.PinnedVersionNumber.HasValue)
        {
            var requestedNo = body.PinnedVersionNumber.Value;
            var exists = await _db.StudyPlans.AsNoTracking()
                .AnyAsync(p => p.StableId == cohort.StudyPlanStableId && p.VersionNumber == requestedNo);
            if (!exists)
            {
                return BadRequest(new { code = "version_not_found", versionNumber = requestedNo });
            }

            cohort.PinnedVersionNumber = requestedNo;
            pinnedNo = requestedNo;
        }

        await _db.SaveChangesAsync();

        if (pinnedNo.HasValue)
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
// Align enrolled users in this cohort to the new PlanVersion
OPTIONAL MATCH (u:User)-[:IN_COHORT]->(c)
WITH c, v AS pv, collect(u) AS users
UNWIND users AS u
MERGE (u)-[:ENROLLED_IN]->(pv)";
                await tx.RunAsync(cypher, new
                {
                    cohortId = cohortId.ToString(),
                    versionNumber = pinnedNo.Value
                });
            });
        }

        return Ok();
    }

    [HttpPost("Cohorts/{cohortId:guid}/UpgradeVersion")] // B5-4 upgrade to plan current
    [Authorize(Policy = "Cohort.Manage")]
    public async Task<IActionResult> UpgradeToCurrent(Guid groupId, Guid cohortId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var isManager = await _groups.IsUserManagerAsync(groupId.ToString(), userId);
        if (!isManager) return Forbid();

        var cohort = await _db.Cohorts.FirstOrDefaultAsync(x => x.Id == cohortId && x.StudyGroupId == groupId);
        if (cohort == null) return NotFound();

        var current = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.StableId == cohort.StudyPlanStableId)
            .OrderByDescending(p => p.IsCurrent)
            .ThenByDescending(p => p.VersionNumber)
            .FirstOrDefaultAsync();

        if (current == null)
        {
            return BadRequest(new { code = "no_versions" });
        }

        var pinnedNo = current.VersionNumber;

        await using (var session = _driver.AsyncSession())
        {
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
// Align enrolled users in this cohort to the new PlanVersion
OPTIONAL MATCH (u:User)-[:IN_COHORT]->(c)
WITH c, v AS pv, collect(u) AS users
UNWIND users AS u
MERGE (u)-[:ENROLLED_IN]->(pv)";
                await tx.RunAsync(cypher, new { cohortId = cohortId.ToString(), versionNumber = pinnedNo });
            });
        }

        cohort.PinnedVersionNumber = pinnedNo;
        await _db.SaveChangesAsync();

        return Ok(new { pinnedVersionNumber = pinnedNo });
    }
}
