using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Sciencetopia.Data;

namespace Sciencetopia.Controllers.Admin;

[ApiController]
[Route("api/admin/graph")]
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

    [HttpPost("backfill")]
    public async Task<IActionResult> Backfill()
    {
        int pinnedLinked = 0;
        int activeLinked = 0;

        // 1) OF_VERSION per cohort from SQL PinnedVersionId -> versionNumber
        var items = await _db.Cohorts.AsNoTracking()
            .Where(c => c.PinnedVersionId != null)
            .Join(_db.StudyPlanVersions,
                  c => c.PinnedVersionId,
                  v => v.Id,
                  (c, v) => new { cohortId = c.Id, planId = c.StudyPlanId, versionNumber = v.VersionNumber })
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
MERGE (v:PlanVersion {studyPlanId:$planId, versionNumber:$versionNumber})
MERGE (c)-[:OF_VERSION]->(v)";
                    await tx.RunAsync(cypher, new { cohortId = it.cohortId, planId = it.planId.ToString(), versionNumber = it.versionNumber });
                    count++;
                }
                return count;
            });
        }

        // 2) Enrollment backfill: ensure ENROLLED_IN to cohort's PlanVersion for users already in cohort
        await using (var session = _driver.AsyncSession())
        {
            activeLinked = await session.ExecuteWriteAsync(async tx =>
            {
                var cypher = @"
MATCH (u:User)-[:IN_COHORT]->(c:Cohort)
MATCH (c)-[:OF_VERSION]->(pv:PlanVersion)
MERGE (u)-[:ENROLLED_IN]->(pv)
RETURN count(*) AS activeLinked
";
                var cursor = await tx.RunAsync(cypher);
                var rec = await cursor.SingleAsync();
                return rec["activeLinked"].As<int>();
            });
        }

        return Ok(new { pinnedLinked, activeLinked });
    }
}
