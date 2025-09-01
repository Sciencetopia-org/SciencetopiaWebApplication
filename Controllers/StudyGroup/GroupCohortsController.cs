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
[Route("api/groups/{groupId:guid}")]
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

    [HttpPost("plans/{planId:guid}/cohorts")] // B5-4 create group-scoped
    [Authorize(Policy = "Plan.Edit")]
    public async Task<IActionResult> Create(Guid groupId, Guid planId, [FromBody] CreateGroupCohortRequest body)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var isManager = await _groups.IsUserManagerAsync(groupId.ToString(), userId);
        if (!isManager) return Forbid();

        // Determine pin target
        var pinned = await _db.StudyPlans.AsNoTracking().Where(p => p.Id == planId).Select(p => p.CurrentVersionId).FirstOrDefaultAsync();
        if (!pinned.HasValue)
        {
            pinned = await _db.StudyPlanVersions.Where(v => v.StudyPlanId == planId).OrderByDescending(v => v.VersionNumber).Select(v => (long?)v.Id).FirstOrDefaultAsync();
        }
        var pinnedNo = pinned.HasValue
            ? await _db.StudyPlanVersions.Where(v => v.Id == pinned.Value).Select(v => v.VersionNumber).FirstOrDefaultAsync()
            : 0;

        var c = new StudyPlanCohort
        {
            Id = Guid.NewGuid(),
            StudyPlanId = planId,
            StudyGroupId = groupId,
            Title = body.Title,
            Visibility = string.IsNullOrEmpty(body.Visibility) ? "group" : body.Visibility,
            EnrollMode = body.EnrollMode ?? CohortEnrollMode.OptIn,
            PinnedVersionId = pinned,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };
        _db.Cohorts.Add(c);
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
                await tx.RunAsync(cypher, new { planId = planId.ToString(), cohortId = c.Id.ToString(), title = c.Title, visibility = c.Visibility, groupId = groupId.ToString(), versionNumber = pinnedNo });
            });
        }

        return Ok(new { cohortId = c.Id });
    }

    public class UpdateGroupCohortRequest
    {
        public CohortEnrollMode? EnrollMode { get; set; }
        public long? PinnedVersionId { get; set; }
        public int? PinnedVersionNumber { get; set; }
    }

    [HttpPatch("cohorts/{cohortId:guid}")] // B5-4 patch enrollMode/pinnedVersion
    [Authorize(Policy = "Cohort.Manage")]
    public async Task<IActionResult> Patch(Guid groupId, Guid cohortId, [FromBody] UpdateGroupCohortRequest body)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var isManager = await _groups.IsUserManagerAsync(groupId.ToString(), userId);
        if (!isManager) return Forbid();

        var c = await _db.Cohorts.FirstOrDefaultAsync(x => x.Id == cohortId && x.StudyGroupId == groupId);
        if (c == null) return NotFound();

        if (body.EnrollMode.HasValue) c.EnrollMode = body.EnrollMode.Value;

        long? pinnedId = c.PinnedVersionId;
        int? pinnedNo = null;
        if (body.PinnedVersionId.HasValue)
        {
            pinnedId = body.PinnedVersionId.Value;
            pinnedNo = await _db.StudyPlanVersions.Where(v => v.Id == pinnedId).Select(v => (int?)v.VersionNumber).FirstOrDefaultAsync();
        }
        else if (body.PinnedVersionNumber.HasValue)
        {
            pinnedNo = body.PinnedVersionNumber.Value;
            pinnedId = await _db.StudyPlanVersions.Where(v => v.StudyPlanId == c.StudyPlanId && v.VersionNumber == pinnedNo.Value).Select(v => (long?)v.Id).FirstOrDefaultAsync();
        }

        c.PinnedVersionId = pinnedId;
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
                await tx.RunAsync(cypher, new { cohortId = cohortId.ToString(), versionNumber = pinnedNo.Value });
            });
        }

        return Ok();
    }

    [HttpPost("cohorts/{cohortId:guid}/upgrade-version")] // B5-4 upgrade to plan current
    [Authorize(Policy = "Cohort.Manage")]
    public async Task<IActionResult> UpgradeToCurrent(Guid groupId, Guid cohortId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var isManager = await _groups.IsUserManagerAsync(groupId.ToString(), userId);
        if (!isManager) return Forbid();

        var c = await _db.Cohorts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cohortId && x.StudyGroupId == groupId);
        if (c == null) return NotFound();

        var pinned = await _db.StudyPlans.AsNoTracking().Where(p => p.Id == c.StudyPlanId).Select(p => p.CurrentVersionId).FirstOrDefaultAsync();
        if (!pinned.HasValue) return BadRequest(new { code = "no_current_version" });
        var pinnedNo = await _db.StudyPlanVersions.Where(v => v.Id == pinned.Value).Select(v => v.VersionNumber).FirstOrDefaultAsync();

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

        // also persist in SQL
        var entity = await _db.Cohorts.FirstOrDefaultAsync(x => x.Id == cohortId);
        if (entity != null)
        {
            entity.PinnedVersionId = pinned;
            await _db.SaveChangesAsync();
        }

        return Ok(new { pinnedVersionId = pinned, pinnedVersionNumber = pinnedNo });
    }
}
