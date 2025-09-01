using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Services;
using System.Security.Claims;

namespace Sciencetopia.Controllers.StudyPlan
{
    [ApiController]
    [Route("api/StudyPlans/{planId:guid}/Lessons")] 
    public class LessonsController : ControllerBase
    {
        private readonly StudyPlanService _svc;
        private readonly PermissionService _perm;

        public LessonsController(StudyPlanService svc, PermissionService perm)
        {
            _svc = svc;
            _perm = perm;
        }

        // Lazy-load lesson details including resources + learned flags
        [HttpGet("{lessonId:guid}")]
        public async Task<IActionResult> GetLesson(Guid planId, Guid lessonId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (!await _perm.CanReadAsync(userId, planId)) return Forbid();

            var dto = await _svc.GetLessonDetailAsync(planId, lessonId, userId);
            if (dto == null) return NotFound();
            return Ok(dto);
        }
    }
}

