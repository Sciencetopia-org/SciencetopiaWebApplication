// File: Controllers/StudyPlanController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sciencetopia.Models;
using Sciencetopia.Services;
using System.Security.Claims;

namespace Sciencetopia.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    // [Authorize] // Ensure only authenticated users can access
    public class StudyPlanController : ControllerBase
    {
        private readonly StudyPlanService _studyPlanService;

        public StudyPlanController(StudyPlanService studyPlanService)
        {
            _studyPlanService = studyPlanService;
        }

        [HttpPost("SaveStudyPlan")]
        public async Task<IActionResult> SaveStudyPlan([FromBody] StudyPlanDTO studyPlanDTO)
        {
            // Retrieve the user's ID from the ClaimsPrincipal
            string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

            // Ensure the user is authenticated
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User is not authenticated.");
            }

            var result = await _studyPlanService.SaveStudyPlanAsync(studyPlanDTO, userId);
            if (result)
            {
                return Ok(); // Plan saved successfully
            }
            else
            {
                return BadRequest("该学习计划已经存在。");
            }
        }

        [HttpGet("FetchStudyPlan")]
        public async Task<IActionResult> FetchStudyPlan()
        {
            string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

            // Ensure the user is authenticated
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User is not authenticated.");
            }

            var studyPlan = await _studyPlanService.GetStudyPlansByUserIdAsync(userId);

            if (studyPlan == null)
            {
                return NotFound("Study plan not found for the user.");
            }

            return Ok(studyPlan);
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
    }
}
