// File: Controllers/StudyPlanController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sciencetopia.Models;
using Sciencetopia.Services;
using System.Security.Claims;
using Sciencetopia.DTOs;

namespace Sciencetopia.Controllers.StudyPlan
{
    [ApiController]
    [Route("api/[controller]")]
    // [Authorize] // Ensure only authenticated users can access
    public class StudyPlanController : ControllerBase
    {
        private readonly StudyPlanService _studyPlanService;
        private readonly PermissionService _permissionService;
        private readonly Sciencetopia.Services.Region.IRegionService _regionService;

        public StudyPlanController(StudyPlanService studyPlanService, PermissionService permissionService, Sciencetopia.Services.Region.IRegionService regionService)
        {
            _studyPlanService = studyPlanService;
            _permissionService = permissionService;
            _regionService = regionService;
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

            var canRead = await _permissionService.CanReadAsync(currentUserId, planGuid);
            if (!canRead)
            {
                return Forbid();
            }

            try
            {
                // Fetch the study plan using the service
                var studyPlan = await _studyPlanService.GetStudyPlanByIdAsync(studyPlanId, currentUserId);

                if (studyPlan == null)
                {
                    return NotFound(new { message = "Study plan not found." });
                }

                // Region-based filtering: if Mainland China, hide blocked resources
                try
                {
                    var isCn = await _regionService.IsMainlandChinaAsync(HttpContext);
                    if (isCn && studyPlan.StudyPlan != null)
                    {
                        void FilterLessons(List<Lesson>? list)
                        {
                            if (list == null) return;
                            foreach (var les in list)
                            {
                                if (les.Resources != null)
                                {
                                    les.Resources = Sciencetopia.Utils.ChinaAccessFilter.FilterResourcesForChina(les.Resources).ToList();
                                }
                            }
                        }

                        FilterLessons(studyPlan.StudyPlan.Prerequisite);
                        FilterLessons(studyPlan.StudyPlan.MainCurriculum);
                        FilterLessons(studyPlan.StudyPlan.AdvancedTopics);
                    }
                }
                catch { /* ignore region failures */ }

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


        [HttpPost("UpdateStudyPlan")]
        public async Task<IActionResult> UpdateStudyPlan([FromBody] StudyPlanDTO studyPlanDTO, [FromQuery] bool createNewVersion = false)
        {
            string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

            // Ensure the user is authenticated
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User is not authenticated.");
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
    }
}
