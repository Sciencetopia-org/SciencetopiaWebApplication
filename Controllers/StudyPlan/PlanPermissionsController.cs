using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Services;

namespace Sciencetopia.Controllers.StudyPlan
{
    [ApiController]
    [Route("api/Plans")] 
    public class PlanPermissionsController : ControllerBase
    {
        private readonly PlanSharingService _sharingService;
        public PlanPermissionsController(PlanSharingService sharingService)
        {
            _sharingService = sharingService;
        }

        [HttpGet("{planId}/Permissions")]
        public async Task<IActionResult> GetPermissions([FromRoute] string planId, [FromQuery] string userId)
        {
            if (string.IsNullOrEmpty(userId)) return BadRequest("userId is required");
            var result = await _sharingService.GetEffectivePermissionsAsync(planId, userId);
            return Ok(result);
        }
    }
}
