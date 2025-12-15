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
        private readonly Sciencetopia.Services.Region.IRegionService _region;

        public LessonsController(StudyPlanService svc, PermissionService perm, Sciencetopia.Services.Region.IRegionService region)
        {
            _svc = svc;
            _perm = perm;
            _region = region;
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
            // Filter resources by region: if Mainland China, hide blocked ones
            try
            {
                var isCn = await _region.IsMainlandChinaAsync(HttpContext);
                if (isCn && dto.Resources != null)
                {
                    dto.Resources = Sciencetopia.Utils.ChinaAccessFilter.FilterResourcesForChina(dto.Resources).ToList();
                }
            }
            catch { /* ignore region failures */ }
            return Ok(dto);
        }
    }
}
