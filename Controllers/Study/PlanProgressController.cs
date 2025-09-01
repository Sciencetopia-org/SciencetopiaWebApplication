using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Sciencetopia.Services.Progress;
using Sciencetopia.Services;

namespace Sciencetopia.Controllers.Study;

[ApiController]
[Route("api")] 
public class PlanProgressController : ControllerBase
{
    private readonly IResourceProgressService _svc;
    private readonly PermissionService _perm;

    public PlanProgressController(IResourceProgressService svc, PermissionService perm)
    {
        _svc = svc;
        _perm = perm;
    }

    [HttpGet("StudyPlans/{planId:guid}/Progress/Me")]
    public async Task<IActionResult> MyPlanProgress(Guid planId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized("User is not authenticated.");

        if (!await _perm.CanReadAsync(userId, planId)) return Forbid();

        var progress = await _svc.GetPlanProgressAsync(userId, planId);
        return Ok(progress);
    }

    [HttpGet("StudyPlans/{planId:guid}/Lessons/{lessonId:guid}/Progress/Me")]
    public async Task<IActionResult> MyLessonProgress(Guid planId, Guid lessonId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized("User is not authenticated.");

        if (!await _perm.CanReadAsync(userId, planId)) return Forbid();

        var progress = await _svc.GetLessonProgressAsync(userId, lessonId);
        return Ok(progress);
    }
}
