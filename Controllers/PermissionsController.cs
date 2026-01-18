using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sciencetopia.DTOs;
using Sciencetopia.Services;

namespace Sciencetopia.Controllers;

[ApiController]
[Route("api/Permissions")] 
public class PermissionsController : ControllerBase
{
    private readonly PermissionService _perm;

    public PermissionsController(PermissionService perm)
    {
        _perm = perm;
    }

    // GET /api/Permissions/Effective?planId=...&cohortId=...&userId=...
    [HttpGet("Effective")]
    [Authorize] // requires a logged-in user; if userId omitted, use current user
    public async Task<ActionResult<EffectivePermissionsDto>> GetEffective([FromQuery] Guid planId, [FromQuery] Guid? cohortId, [FromQuery] string? userId)
    {
        var uid = userId;
        if (string.IsNullOrWhiteSpace(uid))
        {
            uid = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        }

        if (string.IsNullOrWhiteSpace(uid))
        {
            return BadRequest("userId is required or must be authenticated");
        }

        var result = await _perm.GetEffectivePermissionsAsync(uid!, planId, cohortId, HttpContext.RequestAborted);
        return Ok(result);
    }
}
