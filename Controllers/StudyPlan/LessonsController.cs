using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Services;
using Sciencetopia.Services.Progress;
using System.Security.Claims;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Sciencetopia.Controllers.StudyPlan
{
    [ApiController]
    [Route("api/StudyPlans/{planId:guid}/Lessons")] 
    public class LessonsController : ControllerBase
    {
        private readonly StudyPlanService _svc;
        private readonly PermissionService _perm;
        private readonly IResourceProgressService _progress;
        private readonly ILogger<LessonsController> _logger;

        public LessonsController(StudyPlanService svc, PermissionService perm, IResourceProgressService progress, ILogger<LessonsController> logger)
        {
            _svc = svc;
            _perm = perm;
            _progress = progress;
            _logger = logger;
        }

        // Lazy-load lesson details including resources + learned flags
        [HttpGet("{lessonId:guid}")]
        public async Task<IActionResult> GetLesson(Guid planId, Guid lessonId)
        {
            var totalSw = Stopwatch.StartNew();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            var permSw = Stopwatch.StartNew();
            if (!await _perm.CanReadAsync(userId, planId)) return Forbid();
            permSw.Stop();

            var detailSw = Stopwatch.StartNew();
            var dto = await _svc.GetLessonDetailAsync(planId, lessonId, userId);
            detailSw.Stop();
            if (dto == null) return NotFound();

            var progressSw = Stopwatch.StartNew();
            if ((dto.Resources?.Count ?? 0) > 0)
            {
                var completedIds = await _progress.GetCompletedResourceIdsForLessonAsync(userId, lessonId);

                var finishedCount = 0;
                foreach (var resource in dto.Resources ?? Enumerable.Empty<ResourceDTO>())
                {
                    resource.Learned = Guid.TryParse(resource.Id, out var resourceId)
                        && completedIds.Contains(resourceId);
                    if (resource.Learned)
                    {
                        finishedCount++;
                    }
                }

                dto.FinishedResourcesCount = finishedCount;
            }
            progressSw.Stop();
            totalSw.Stop();
            _logger.LogInformation(
                "LessonsController.GetLesson completed. planId={PlanId} lessonId={LessonId} permissionMs={PermissionMs} detailMs={DetailMs} progressMs={ProgressMs} totalMs={TotalMs}",
                planId,
                lessonId,
                permSw.ElapsedMilliseconds,
                detailSw.ElapsedMilliseconds,
                progressSw.ElapsedMilliseconds,
                totalSw.ElapsedMilliseconds);

            return Ok(dto);
        }
    }
}
