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

        public StudyPlanController(StudyPlanService studyPlanService, PermissionService permissionService)
        {
            _studyPlanService = studyPlanService;
            _permissionService = permissionService;
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
        public async Task<IActionResult> UpdateStudyPlan([FromBody] StudyPlanDTO studyPlanDTO)
        {
            string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

            // Ensure the user is authenticated
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User is not authenticated.");
            }

            var success = await _studyPlanService.UpdateStudyPlanAsync(studyPlanDTO);
            if (success)
            {
                return Ok(new { message = "Study plan updated successfully." });
            }
            else
            {
                return NotFound(new { message = "Study plan not found or could not be updated." });
            }
        }

        [HttpPost("SaveStudyPlanDraft")]
        public async Task<IActionResult> SaveStudyPlanDraft([FromBody] SaveStudyPlanDraftRequest request)
        {
            string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized("User is not authenticated.");
            if (request?.Payload?.StudyPlan?.Id == null) return BadRequest("studyPlanId is required.");

            var (ok, draftNumber) = await _studyPlanService.SaveStudyPlanDraftAsync(request.Payload!, userId, request.ChangeNotes);
            if (!ok) return BadRequest("Failed to save draft.");
            return Ok(new { draftNumber });
        }

        [HttpPost("PublishStudyPlanDraft")]
        public async Task<IActionResult> PublishStudyPlanDraft([FromBody] PublishStudyPlanDraftRequest request)
        {
            string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized("User is not authenticated.");
            if (string.IsNullOrEmpty(request?.StudyPlanId) || request.DraftNumber <= 0)
                return BadRequest("Invalid studyPlanId or draftNumber.");

            var (ok, versionNumber) = await _studyPlanService.PublishStudyPlanDraftAsync(request.StudyPlanId!, request.DraftNumber, userId, request.ChangeNotes);
            if (!ok) return BadRequest("Failed to publish draft.");
            return Ok(new { versionNumber });
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
            var success = await _studyPlanService.SetStudyPlanPrivacyAsync(userId, request.StudyPlanId, request.Privacy);

            if (!success)
            {
                return NotFound("Study plan not found or could not be updated.");
            }

            return Ok("Privacy updated successfully.");
        }
    }
}
