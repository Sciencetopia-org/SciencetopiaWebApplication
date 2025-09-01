using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sciencetopia.DTOs;
using Sciencetopia.Services;
using System.Security.Claims;

namespace Sciencetopia.Controllers.StudyGroups
{
    [ApiController]
    [Route("api/StudyGroups/{StudyGroupId}/Plans/{PlanId}")]
    public class StudyGroupPlansController : ControllerBase
    {
        private readonly PlanSharingService _sharingService;

        public StudyGroupPlansController(PlanSharingService sharingService)
        {
            _sharingService = sharingService;
        }

        [HttpPost("share")]
        public async Task<IActionResult> ShareToStudyGroup([FromRoute(Name = "StudyGroupId")] string studyGroupId, [FromRoute(Name = "PlanId")] string planId, [FromBody] ShareStudyPlanRequest request)
        {
            var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized("User is not authenticated.");

            var rec = await _sharingService.ShareToStudyGroupAsync(studyGroupId, planId, request.Permission, request.AutoEnroll, request.UseDraftFlow, userId);
            return Ok(rec);
        }

        [HttpDelete]
        public async Task<IActionResult> UnshareFromStudyGroup([FromRoute(Name = "StudyGroupId")] string studyGroupId, [FromRoute(Name = "PlanId")] string planId)
        {
            var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized("User is not authenticated.");

            var ok = await _sharingService.UnshareFromStudyGroupAsync(studyGroupId, planId);
            return ok ? Ok(new { message = "Unshared" }) : NotFound();
        }
    }
}
