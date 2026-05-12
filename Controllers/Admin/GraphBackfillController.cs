using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Sciencetopia.Data;

namespace Sciencetopia.Controllers.Admin;

[ApiController]
[Route("api/Admin/Graph")]
[Authorize(Policy = "RequireAdministratorRole")]
public class GraphBackfillController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IDriver _driver;

    public GraphBackfillController(ApplicationDbContext db, IDriver driver)
    {
        _db = db;
        _driver = driver;
    }

    [HttpPost("Backfill")]
    public async Task<IActionResult> Backfill()
    {
        int pinnedLinked = 0;
        int activeLinked = 0;
        int personalGroupsProjected = 0;
        int completedMoved = 0;
        int enrollmentMoved = 0;
        int favoritesMoved = 0;

        // 1) OF_VERSION per cohort from SQL plan version binding
        var items = await _db.Cohorts.AsNoTracking()
            .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                c => c.StudyGroupStudyPlanId,
                sgsp => sgsp.Id,
                (c, sgsp) => new { c.Id, c.StudyPlanVersionId, sgsp.StudyPlanStableId })
            .Join(_db.StudyPlans.AsNoTracking(),
                co => co.StudyPlanVersionId,
                p => p.Id,
                (co, p) => new
                {
                    cohortId = co.Id,
                    planId = co.StudyPlanStableId != Guid.Empty ? co.StudyPlanStableId : (p.StableId == Guid.Empty ? p.Id : p.StableId),
                    versionNumber = p.VersionNumber
                })
            .ToListAsync();

        await using (var session = _driver.AsyncSession())
        {
            pinnedLinked = await session.ExecuteWriteAsync(async tx =>
            {
                int count = 0;
                foreach (var it in items)
                {
                    var cypher = @"
MATCH (c:Cohort {id:$cohortId})
MATCH (p:StudyPlan {id:$planId})
MERGE (c)-[:FOR_PLAN]->(p)
WITH c, $planId AS planId, $versionNumber AS versionNumber
OPTIONAL MATCH (c)-[old:OF_VERSION]->(:PlanVersion)
DELETE old
WITH c, planId, versionNumber
MERGE (v:PlanVersion {studyPlanId:planId, versionNumber:versionNumber})
MERGE (c)-[:OF_VERSION]->(v)";
                    await tx.RunAsync(cypher, new
                    {
                        cohortId = it.cohortId.ToString(),
                        planId = it.planId.ToString(),
                        versionNumber = it.versionNumber
                    });
                    count++;
                }
                return count;
            });
        }

        var personalGroups = await _db.UserGroups.AsNoTracking()
            .Where(ug => ug.Status == "Active")
            .Join(_db.Groups.AsNoTracking().Where(g => g.Kind == "PersonalGroup"),
                ug => ug.GroupId,
                g => g.Id,
                (ug, g) => new { ug.UserId, GroupId = g.Id })
            .ToListAsync();

        await using (var session = _driver.AsyncSession())
        {
            personalGroupsProjected = await session.ExecuteWriteAsync(async tx =>
            {
                var cypher = @"
UNWIND $rows AS row
MERGE (u:User {id:row.userId})
MERGE (g:Group {id:row.groupId})
SET g.kind = 'PersonalGroup'
MERGE (u)-[:MEMBER_OF]->(g)
RETURN count(DISTINCT row.groupId) AS count";
                var cursor = await tx.RunAsync(cypher, new
                {
                    rows = personalGroups.Select(x => new { x.UserId, groupId = x.GroupId.ToString() }).ToList()
                });
                var rec = await cursor.SingleAsync();
                return rec["count"].As<int>();
            });
        }

        // 2) Enrollment backfill: ensure each user's PersonalGroup enrolls in cohort's PlanVersion
        await using (var session = _driver.AsyncSession())
        {
            activeLinked = await session.ExecuteWriteAsync(async tx =>
            {
                var cypher = @"
MATCH (u:User)-[:IN_COHORT]->(c:Cohort)
MATCH (u)-[:MEMBER_OF]->(pg:Group {kind:'PersonalGroup'})
MATCH (c)-[:OF_VERSION]->(pv:PlanVersion)
MERGE (pg)-[:ENROLLED_IN]->(pv)
RETURN count(*) AS activeLinked
";
                var cursor = await tx.RunAsync(cypher);
                var rec = await cursor.SingleAsync();
                return rec["activeLinked"].As<int>();
            });
        }

        await using (var session = _driver.AsyncSession())
        {
            completedMoved = await session.ExecuteWriteAsync(async tx =>
            {
                var cursor = await tx.RunAsync(@"
MATCH (u:User)-[:MEMBER_OF]->(pg:Group {kind:'PersonalGroup'})
MATCH (u)-[r:COMPLETED]->(target)
WHERE target:Resource OR target:StudyPlan
MERGE (pg)-[moved:COMPLETED]->(target)
SET moved.completedAt = coalesce(moved.completedAt, r.completedAt, r.at),
    moved.source = coalesce(moved.source, r.source),
    moved.device = coalesce(moved.device, r.device),
    moved.spentSeconds = coalesce(moved.spentSeconds, 0) + coalesce(r.spentSeconds, 0)
DELETE r
RETURN count(*) AS count");
                var rec = await cursor.SingleAsync();
                return rec["count"].As<int>();
            });

            enrollmentMoved = await session.ExecuteWriteAsync(async tx =>
            {
                var cursor = await tx.RunAsync(@"
MATCH (u:User)-[:MEMBER_OF]->(pg:Group {kind:'PersonalGroup'})
MATCH (u)-[r:ENROLLED_IN]->(pv:PlanVersion)
MERGE (pg)-[:ENROLLED_IN]->(pv)
DELETE r
RETURN count(*) AS count");
                var rec = await cursor.SingleAsync();
                return rec["count"].As<int>();
            });

            favoritesMoved = await session.ExecuteWriteAsync(async tx =>
            {
                var cursor = await tx.RunAsync(@"
MATCH (u:User)-[:MEMBER_OF]->(pg:Group {kind:'PersonalGroup'})
MATCH (u)-[r:OWNS]->(f:Favorite)
MERGE (pg)-[:OWNS]->(f)
DELETE r
RETURN count(*) AS count");
                var rec = await cursor.SingleAsync();
                return rec["count"].As<int>();
            });
        }

        return Ok(new { pinnedLinked, personalGroupsProjected, activeLinked, completedMoved, enrollmentMoved, favoritesMoved });
    }
}
