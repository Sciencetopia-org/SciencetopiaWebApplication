using Microsoft.AspNetCore.Mvc;
using Neo4j.Driver;
using Sciencetopia.Models;
using System;
using System.Threading.Tasks;

[Route("api/[controller]")]
[ApiController]
public class StudyPlanController : ControllerBase
{
    private readonly IAsyncSession _session;
    
    public StudyPlanController(IAsyncSession session)
    {
        _session = session;
    }

    // POST: api/StudyPlan/SavePlan
    [HttpPost("save")]
    public async Task<IActionResult> SaveStudyPlan([FromBody] StudyPlanModel studyPlan)
    {
        try
        {
            var result = await _session.ExecuteWriteAsync(async tx =>
            {
                var query = @"
            MATCH (u:User {Id: $userId})
            CREATE (u)-[:HAS_STUDY_PLAN]->(p:StudyPlan {name: $name, content: $content})
            RETURN p";

                var parameters = new { userId = studyPlan.UserId, name = studyPlan.Name, content = studyPlan.Content };
                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.SingleAsync();
            });

            // Handle the result as needed
            return Ok(new { success = true, studyPlan = result });
        }
        catch (Exception ex)
        {
            // Handle exceptions
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

}
