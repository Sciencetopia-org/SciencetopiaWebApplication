using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Services;

namespace Sciencetopia.Controllers.StudyPlan
{
    [ApiController]
    [Route("api/Plans")]
    public class PlanPermissionsController : ControllerBase
    {
        private readonly PermissionService _permissionService;
        public PlanPermissionsController(PermissionService permissionService)
        {
            _permissionService = permissionService;
        }

        [HttpGet("{planId}/Permissions")]
        public async Task<IActionResult> GetPermissions([FromRoute] string planId, [FromQuery] string userId)
        {
            if (string.IsNullOrEmpty(userId)) return BadRequest("userId is required");
            if (!Guid.TryParse(planId, out var planGuid)) return BadRequest("Invalid planId.");
            var result = await _permissionService.GetEffectivePermissionsAsync(userId, planGuid, null, HttpContext.RequestAborted);
            return Ok(result);
        }
    }
}
