using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Sciencetopia.DTOs;
using Sciencetopia.Services.Cohorts;
using Sciencetopia.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Sciencetopia.Hubs;

namespace Sciencetopia.Controllers.Study;

[ApiController]
[Route("api")] 
public class CohortsController : ControllerBase
{
    private readonly ICohortService _svc;
    private readonly PermissionService _perm;
    private readonly StudyGroupService _groups;
    private readonly IHubContext<StudyHub> _hub;

    public CohortsController(ICohortService svc, PermissionService perm, StudyGroupService groups, IHubContext<StudyHub> hub)
    {
        _svc = svc;
        _perm = perm;
        _groups = groups;
        _hub = hub;
    }

    [HttpGet("studyPlans/{planId:guid}/cohorts")]
    public async Task<IActionResult> ListByPlan(Guid planId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (!await _perm.CanReadAsync(userId, planId)) return Forbid();
        var list = await _svc.ListByPlanAsync(planId);
        return Ok(list);
    }

    [HttpPost("studyPlans/{planId:guid}/cohorts")]
    [Authorize(Policy = "Plan.Edit")]
    public async Task<IActionResult> Create(Guid planId, [FromBody] CohortCreateDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var v = await _svc.CreateAsync(planId, userId, dto);
        return Ok(v);
    }

    

    [HttpPut("cohorts/{cohortId:guid}")]
    [Authorize(Policy = "Cohort.Manage")]
    public async Task<IActionResult> Update(Guid cohortId, [FromBody] CohortUpdateDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var updated = await _svc.UpdateAsync(cohortId, dto);
        return Ok(updated);
    }

    [HttpDelete("cohorts/{cohortId:guid}")]
    [Authorize(Policy = "Cohort.Manage")]
    public async Task<IActionResult> Delete(Guid cohortId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        await _svc.DeleteAsync(cohortId, HttpContext.RequestAborted);
        return NoContent();
    }

    // Removed: POST /cohorts/{cohortId}/enroll (deprecated)

    [HttpDelete("cohorts/{cohortId:guid}/leave")]
    public async Task<IActionResult> Unenroll(Guid cohortId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        await _svc.UnenrollAsync(cohortId, userId);
        return Ok();
    }

    // B5-1: POST /cohorts/{cohortId}/join
    [HttpPost("cohorts/{cohortId:guid}/join")]
    public async Task<IActionResult> Join(Guid cohortId, [FromBody] JoinCohortRequest body)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        try
        {
            var (planId, cid) = await _svc.JoinCohortAsync(cohortId, userId, body.ShareMetrics ?? true, HttpContext.RequestAborted);
            await _hub.Clients.All.SendAsync("cohort_joined", new { planId, cohortId = cid, userId });
            return Ok(new JoinCohortResponse { PlanId = planId, CohortId = cid });
        }
        catch (InvalidOperationException ex) when (ex.Message == "alreadyInPlan")
        {
            return Conflict(new { code = "alreadyInPlan" });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    // B5-2: POST /plans/{planId}/switch-cohort
    [HttpPost("plans/{planId:guid}/switch-cohort")]
    public async Task<IActionResult> Switch(Guid planId, [FromBody] SwitchCohortRequest body)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        try
        {
            var (pid, fromC, toC) = await _svc.SwitchCohortAsync(planId, body.ToCohortId, userId, body.ShareMetrics ?? true, body.MigrationStrategy, HttpContext.RequestAborted);
            await _hub.Clients.All.SendAsync("cohort_switched", new { planId = pid, from = fromC, to = toC, userId });
            return Ok(new SwitchCohortResponse { PlanId = pid, FromCohortId = fromC, ToCohortId = toC });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex) when (ex.Message == "cohort_plan_mismatch")
        {
            return BadRequest(new { code = "cohort_plan_mismatch" });
        }
    }

    [HttpPost("cohorts/{cohortId:guid}/autoEnroll/{groupId:guid}")]
    public async Task<IActionResult> AutoEnroll(Guid cohortId, Guid groupId, [FromQuery] bool run = false)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var isManager = await _groups.IsUserManagerAsync(groupId.ToString(), userId);
        if (!isManager) return Forbid();
        await _svc.AutoEnrollGroupAsync(cohortId, groupId, true);
        if (run)
        {
            var processed = await _svc.RunAutoEnrollAsync(cohortId, groupId, HttpContext.RequestAborted);
            return Ok(new { processed });
        }
        return Ok();
    }

    [HttpDelete("cohorts/{cohortId:guid}/autoEnroll/{groupId:guid}")]
    public async Task<IActionResult> RemoveAutoEnroll(Guid cohortId, Guid groupId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var isManager = await _groups.IsUserManagerAsync(groupId.ToString(), userId);
        if (!isManager) return Forbid();
        await _svc.RemoveAutoEnrollGroupAsync(cohortId, groupId);
        return Ok();
    }
}
