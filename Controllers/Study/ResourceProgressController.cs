using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Sciencetopia.DTOs;
using Sciencetopia.Services.Progress;
using Sciencetopia.Services;
using Sciencetopia.DTOs;

namespace Sciencetopia.Controllers.Study;

[ApiController]
[Route("api/Resources")]
public class ResourceProgressController : ControllerBase
{
    private readonly IResourceProgressService _svc;
    private readonly PermissionService _perm;

    public ResourceProgressController(IResourceProgressService svc, PermissionService perm)
    {
        _svc = svc;
        _perm = perm;
    }

    [HttpPost("CompletedStatus")]
    public async Task<IActionResult> CompletedStatus([FromBody] ResourcesStatusRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized("User is not authenticated.");
        var ids = req?.resourceIds ?? Enumerable.Empty<Guid>();
        var result = await _svc.GetCompletedStatusAsync(userId, ids);
        return Ok(result);
    }

    [HttpPost("{resourceId:guid}/Complete")]
    public async Task<IActionResult> Complete(Guid resourceId, [FromBody] CompleteResourceDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized("User is not authenticated.");

        if (dto.planId.HasValue)
        {
            var allowed = await _perm.CanReadAsync(userId, dto.planId.Value);
            if (!allowed) return Forbid();
        }

        var res = await _svc.CompleteAsync(userId, resourceId, dto);
        return Ok(res);
    }

    [HttpDelete("{resourceId:guid}/Complete")]
    public async Task<IActionResult> Undo(Guid resourceId, [FromQuery] Guid? planId = null, [FromQuery] Guid? lessonId = null)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized("User is not authenticated.");

        if (planId.HasValue)
        {
            var allowed = await _perm.CanReadAsync(userId, planId.Value);
            if (!allowed) return Forbid();
        }

        await _svc.UndoAsync(userId, resourceId);

        // Compute updated progress to allow immediate UI refresh
        double planProgress = 0;
        double lessonProgress = 0;
        int lessonCompleted = 0;
        int lessonTotal = 0;

        if (planId.HasValue)
        {
            var plan = await _svc.GetPlanProgressAsync(userId, planId.Value);
            planProgress = plan.planProgress;
        }
        if (lessonId.HasValue)
        {
            var les = await _svc.GetLessonProgressAsync(userId, lessonId.Value);
            lessonProgress = les.lessonProgress;
            lessonCompleted = les.completedCount;
            lessonTotal = les.totalResources;
        }

        return Ok(new ResourceProgressResult(planProgress, lessonProgress, lessonCompleted, lessonTotal));
    }
}
