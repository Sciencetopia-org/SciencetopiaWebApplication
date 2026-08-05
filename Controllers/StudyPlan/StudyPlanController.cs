// File: Controllers/StudyPlanController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sciencetopia.Models;
using Sciencetopia.Services;
using Sciencetopia.Services.Progress;
using System.Security.Claims;
using Sciencetopia.DTOs;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sciencetopia.Services.ContentSafety;

namespace Sciencetopia.Controllers.StudyPlan
{
    [ApiController]
    [Route("api/[controller]")]
    // [Authorize] // Ensure only authenticated users can access
    public class StudyPlanController : ControllerBase
    {
        private readonly StudyPlanService _studyPlanService;
        private readonly PermissionService _permissionService;
        private readonly IResourceProgressService _progressService;
        private readonly ILogger<StudyPlanController> _logger;
        private readonly IContentModerationService _contentModeration;

        public StudyPlanController(
            StudyPlanService studyPlanService,
            PermissionService permissionService,
            IResourceProgressService progressService,
            ILogger<StudyPlanController> logger,
            IContentModerationService contentModeration)
        {
            _studyPlanService = studyPlanService;
            _permissionService = permissionService;
            _progressService = progressService;
            _logger = logger;
            _contentModeration = contentModeration;
        }

        [HttpPost("SaveStudyPlan")]
        public async Task<IActionResult> SaveStudyPlan([FromBody] StudyPlanDTO studyPlanDTO, [FromQuery] bool autoTag = false)
        {
            // Retrieve the user's ID from the ClaimsPrincipal
            string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

            // Ensure the user is authenticated
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User is not authenticated.");
            }

            var moderation = await _contentModeration.ReviewTextAsync(ExtractStudyPlanText(studyPlanDTO), HttpContext.RequestAborted);
            if (!moderation.Allowed)
            {
                return BadRequest(new
                {
                    message = "内容未通过审核，请修改后再发布。",
                    reason = moderation.Reason,
                    blockedCategories = moderation.BlockedCategories
                });
            }

            var id = await _studyPlanService.SaveStudyPlanAsync(studyPlanDTO, userId, autoTag);
            if (!string.IsNullOrEmpty(id))
            {
                return Ok(new { studyPlanId = id }); // Plan saved successfully
            }
            return BadRequest("该学习计划已经存在。");
        }

        [HttpGet("FetchStudyPlans")]
        public async Task<IActionResult> FetchStudyPlans([FromQuery] string? targetUserId = null)
        {
            string currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(currentUserId))
            {
                return Unauthorized("User is not authenticated.");
            }

            targetUserId ??= currentUserId;

            var studyPlans = await _studyPlanService.GetStudyPlansByUserIdAsync(currentUserId, targetUserId);

            if (studyPlans == null || !studyPlans.Any())
            {
                return Ok(new List<StudyPlanDTO>());  // Return an empty list if no study plans are found
            }

            return Ok(studyPlans);
        }

        [HttpGet("GetStudyPlanById")]
        public async Task<IActionResult> GetStudyPlanById([FromQuery] string studyPlanId)
        {
            var totalSw = Stopwatch.StartNew();
            // Fetch the current authenticated user's ID from claims
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(currentUserId))
            {
                return Unauthorized("User is not authenticated.");
            }

            // Permission check via PermissionService
            if (!Guid.TryParse(studyPlanId, out var planGuid))
            {
                return BadRequest(new { message = "Invalid studyPlanId." });
            }

            var permSw = Stopwatch.StartNew();
            var canRead = await _permissionService.CanReadAsync(currentUserId, planGuid);
            permSw.Stop();
            if (!canRead)
            {
                return Forbid();
            }

            try
            {
                // Fetch the study plan using the service
                var detailSw = Stopwatch.StartNew();
                var studyPlan = await _studyPlanService.GetStudyPlanByIdAsync(studyPlanId, currentUserId);
                detailSw.Stop();

                if (studyPlan == null)
                {
                    return NotFound(new { message = "Study plan not found." });
                }

                var detail = studyPlan.StudyPlan;
                if (detail != null)
                {
                    var enrichSw = Stopwatch.StartNew();
                    await ApplyComputedProgressAsync(currentUserId, detail);
                    enrichSw.Stop();
                    totalSw.Stop();
                    _logger.LogInformation(
                        "StudyPlanController.GetStudyPlanById completed. planId={PlanId} permissionMs={PermissionMs} detailMs={DetailMs} enrichMs={EnrichMs} totalMs={TotalMs}",
                        studyPlanId,
                        permSw.ElapsedMilliseconds,
                        detailSw.ElapsedMilliseconds,
                        enrichSw.ElapsedMilliseconds,
                        totalSw.ElapsedMilliseconds);
                }

                return Ok(studyPlan);
            }
            catch (Neo4j.Driver.ServiceUnavailableException)
            {
                // Neo4j (graph store) is unavailable — return a clear 503 for clients to retry later
                return StatusCode(503, new { message = "Graph database is temporarily unavailable. Please try again later." });
            }
            catch (Neo4j.Driver.ConnectionReadTimeoutException)
            {
                // Neo4j read timed out — return 504 so clients can retry
                return StatusCode(504, new { message = "Graph database request timed out. Please try again." });
            }
        }

        private async Task ApplyComputedProgressAsync(string userId, StudyPlanDetail detail)
        {
            var allLessons = EnumerateLessons(detail).ToList();
            HashSet<Guid> completedResourceIds;
            if (Guid.TryParse(detail.StableId, out var planStableId) && planStableId != Guid.Empty)
            {
                completedResourceIds = await _progressService.GetCompletedResourceIdsForPlanAsync(userId, planStableId);
            }
            else
            {
                var resourceIds = allLessons
                    .SelectMany(lesson => lesson.Resources ?? Enumerable.Empty<ResourceDTO>())
                    .Select(resource => Guid.TryParse(resource.Id, out var resourceId) ? resourceId : Guid.Empty)
                    .Where(resourceId => resourceId != Guid.Empty)
                    .Distinct()
                    .ToList();

                if (resourceIds.Count == 0)
                {
                    completedResourceIds = new HashSet<Guid>();
                }
                else
                {
                    var completedStatuses = await _progressService.GetCompletedStatusAsync(userId, resourceIds);
                    completedResourceIds = completedStatuses
                        .Where(x => x.completed)
                        .Select(x => x.resourceId)
                        .ToHashSet();
                }
            }

            var totalResources = 0;
            var completedResources = 0;
            var advancedResources = 0;
            var advancedCompletedResources = 0;
            foreach (var lesson in allLessons)
            {
                if (lesson.Resources == null || lesson.Resources.Count == 0)
                {
                    lesson.ProgressPercentage = 0;
                    lesson.FinishedResourcesCount = 0;
                    continue;
                }

                var finishedCount = 0;
                foreach (var resource in lesson.Resources)
                {
                    resource.Learned = Guid.TryParse(resource.Id, out var resourceId)
                        && completedResourceIds.Contains(resourceId);
                    if (resource.Learned)
                    {
                        finishedCount++;
                    }
                }

                lesson.FinishedResourcesCount = finishedCount;
                lesson.ProgressPercentage = lesson.Resources.Count == 0
                    ? 0
                    : (float)finishedCount / lesson.Resources.Count * 100f;

                totalResources += lesson.Resources.Count;
                completedResources += finishedCount;
            }

            foreach (var lesson in detail.AdvancedTopics ?? Enumerable.Empty<Lesson>())
            {
                var count = lesson.Resources?.Count ?? 0;
                var finished = lesson.FinishedResourcesCount;
                advancedResources += count;
                advancedCompletedResources += finished;
            }

            detail.ProgressPercentage = totalResources == 0 ? 0 : (float)completedResources / totalResources * 100f;
            detail.AdvancedTopicProgressPercentage = advancedResources == 0 ? 0 : (float)advancedCompletedResources / advancedResources * 100f;
        }

        private static IEnumerable<Lesson> EnumerateLessons(StudyPlanDetail detail)
        {
            foreach (var lesson in detail.Prerequisite ?? Enumerable.Empty<Lesson>())
            {
                yield return lesson;
            }

            foreach (var lesson in detail.MainCurriculum ?? Enumerable.Empty<Lesson>())
            {
                yield return lesson;
            }

            foreach (var lesson in detail.AdvancedTopics ?? Enumerable.Empty<Lesson>())
            {
                yield return lesson;
            }
        }


        [HttpPost("UpdateStudyPlan")]
        public async Task<IActionResult> UpdateStudyPlan([FromBody] StudyPlanDTO studyPlanDTO, [FromQuery] bool createNewVersion = false)
        {
            string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

            // Ensure the user is authenticated
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User is not authenticated.");
            }

            var moderation = await _contentModeration.ReviewTextAsync(ExtractStudyPlanText(studyPlanDTO), HttpContext.RequestAborted);
            if (!moderation.Allowed)
            {
                return BadRequest(new
                {
                    message = "内容未通过审核，请修改后再发布。",
                    reason = moderation.Reason,
                    blockedCategories = moderation.BlockedCategories
                });
            }

            var (success, planId) = await _studyPlanService.UpdateStudyPlanAsync(studyPlanDTO, userId, createNewVersion);
            if (!success)
            {
                return NotFound(new { message = "Study plan not found or could not be updated." });
            }

            return Ok(new
            {
                studyPlanId = planId,
                versionUpdated = createNewVersion
            });
        }

        [HttpPost("MarkStudyPlanAsCompleted")]
        public async Task<IActionResult> MarkStudyPlanAsCompleted(string studyPlanTitle)
        {
            string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

            // Ensure the user is authenticated
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User is not authenticated.");
            }

            var success = await _studyPlanService.MarkStudyPlanAsCompletedAsync(studyPlanTitle, userId);
            if (success)
            {
                return Ok(new { message = "Study plan marked as completed." });
            }
            else
            {
                return NotFound(new { message = "Study plan not found or could not be marked as completed." });
            }
        }

        [HttpDelete("DisMarkStudyPlanAsCompleted")]
        public async Task<IActionResult> DisMarkStudyPlanAsCompleted(string studyPlanTitle)
        {
            string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

            // Ensure the user is authenticated
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User is not authenticated.");
            }

            var success = await _studyPlanService.DisMarkStudyPlanAsCompletedAsync(studyPlanTitle, userId);
            if (success)
            {
                return Ok(new { message = "Removed marking study plan as completed." });
            }
            else
            {
                return NotFound(new { message = "Study plan not found or mark of completion could not be removed." });
            }
        }

        [HttpGet("CountCompletedStudyPlansByUserId")]
        public async Task<IActionResult> CountCompletedStudyPlans(string userId)
        {
            // Ensure the user is authenticated
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User is not authenticated.");
            }

            var count = await _studyPlanService.CountCompletedStudyPlansAsync(userId);

            return Ok(count);
        }

        [HttpDelete("DeleteStudyPlan")]
        public async Task<IActionResult> DeleteStudyPlan(string studyPlanTitle)
        {
            // Fetch the current authenticated user's ID from claims
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(currentUserId))
            {
                return Unauthorized(new { message = "User is not authenticated." });
            }

            var success = await _studyPlanService.DeleteStudyPlanAsync(studyPlanTitle, currentUserId);
            if (success)
            {
                return Ok(new { message = "Study plan deleted successfully." });
            }
            else
            {
                return NotFound(new { message = "Study plan not found or could not be deleted." });
            }
        }

        [HttpPost("SetStudyPlanPrivacy")]
        public async Task<IActionResult> SetStudyPlanPrivacy([FromBody] SetPrivacyRequest request)
        {
            string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Ensure the user is authenticated
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User is not authenticated.");
            }

            // Call the service method to update the privacy setting
            var success = await _studyPlanService.SetStudyPlanPrivacyAsync(userId, request.PlanStableId, request.Privacy);

            if (!success)
            {
                return NotFound("Study plan not found or could not be updated.");
            }

            return Ok("Privacy updated successfully.");
        }

        private static IEnumerable<string?> ExtractStudyPlanText(StudyPlanDTO? dto)
        {
            var detail = dto?.StudyPlan;
            if (detail == null)
            {
                yield break;
            }

            yield return detail.Title;
            yield return detail.Introduction?.Description;

            foreach (var tag in detail.Tags ?? Enumerable.Empty<TagDTO>())
            {
                yield return tag.Name;
            }

            foreach (var lesson in EnumerateLessons(detail))
            {
                yield return lesson.Name;
                yield return lesson.Description;

                foreach (var tag in lesson.Tags ?? Enumerable.Empty<TagDTO>())
                {
                    yield return tag.Name;
                }

                foreach (var resource in lesson.Resources ?? Enumerable.Empty<ResourceDTO>())
                {
                    yield return resource.Name;
                }
            }
        }
    }
}
