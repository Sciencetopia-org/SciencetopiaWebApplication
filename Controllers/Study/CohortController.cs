using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Services.Progress;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Models;
using Sciencetopia.Services.Cohorts;

namespace Sciencetopia.Controllers.Study;

[ApiController]
[Route("api/cohorts")] 
public class CohortController : ControllerBase
{
    private readonly IResourceProgressService _svc;
    private readonly ApplicationDbContext _db;
    private readonly ICohortService _cohorts;

    public CohortController(IResourceProgressService svc, ApplicationDbContext db, ICohortService cohorts)
    {
        _svc = svc;
        _db = db;
        _cohorts = cohorts;
    }

    [HttpGet("{cohortId:guid}/stats/summary")]
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

        int? pinnedVersionNumber = null;
        if (c.PinnedVersionId.HasValue)
        {
            pinnedVersionNumber = await _db.StudyPlanVersions
                .Where(v => v.Id == c.PinnedVersionId.Value)
                .Select(v => (int?)v.VersionNumber)
                .FirstOrDefaultAsync();
        }

        return Ok(new
        {
            id = c.Id,
            studyPlanId = c.StudyPlanId,
            studyGroupId = c.StudyGroupId,
            enrollMode = c.EnrollMode.ToString(),
            pinnedVersionId = c.PinnedVersionId,
            pinnedVersionNumber,
            membersCount = c.MembersCount,
            createdAt = c.CreatedAt,
            createdBy = c.CreatedBy,
            title = c.Title,
            visibility = c.Visibility
        });
    }

    // B3-3: AutoEnroll batch trigger
    [HttpPost("{cohortId:guid}/autoEnroll/{groupId:guid}/run")]
    public async Task<IActionResult> RunAutoEnroll(Guid cohortId, Guid groupId)
    {
        var processed = await _cohorts.RunAutoEnrollAsync(cohortId, groupId, HttpContext.RequestAborted);
        return Ok(new { processed });
    }

    [HttpGet("{cohortId:guid}/stats/lessons")]
    public async Task<IActionResult> LessonStats(Guid cohortId)
    {
        var list = await _svc.GetCohortLessonStatsAsync(cohortId);
        return Ok(list);
    }

    [HttpGet("{cohortId:guid}/stats/leaderboard")]
    public async Task<IActionResult> Leaderboard(Guid cohortId, [FromQuery] int top = 20)
    {
        var list = await _svc.GetCohortLeaderboardAsync(cohortId, top);
        return Ok(list);
    }
}
