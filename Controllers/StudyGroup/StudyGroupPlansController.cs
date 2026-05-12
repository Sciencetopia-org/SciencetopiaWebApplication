using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.DTOs;
using Sciencetopia.Services;
using System.Security.Claims;

namespace Sciencetopia.Controllers.StudyGroups
{
    [ApiController]
    [Route("api/StudyGroups/{StudyGroupId}/Plans")]
    public class StudyGroupPlansController : ControllerBase
    {
        private readonly PlanSharingService _sharingService;
        private readonly StudyGroupService _groups;
        private readonly PermissionService _permissions;
        private readonly ApplicationDbContext _db;

        public StudyGroupPlansController(PlanSharingService sharingService, StudyGroupService groups, PermissionService permissions, ApplicationDbContext db)
        {
            _sharingService = sharingService;
            _groups = groups;
            _permissions = permissions;
            _db = db;
        }

        private async Task<Guid> ResolveStableIdAsync(Guid identifier)
        {
            var byId = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.Id == identifier)
                .Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
                .FirstOrDefaultAsync();
            if (byId != Guid.Empty) return byId;

            var hasStable = await _db.StudyPlans.AsNoTracking()
                .AnyAsync(p => p.StableId == identifier);
            return hasStable ? identifier : Guid.Empty;
        }

        [HttpGet]
        public async Task<IActionResult> GetSharedPlans([FromRoute(Name = "StudyGroupId")] string studyGroupId)
        {
            var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized("User is not authenticated.");
            var isMember = await _groups.IsUserMemberAsync(studyGroupId, userId);
            if (!isMember) return Forbid();

            var plans = await _sharingService.GetSharedPlansForStudyGroupAsync(studyGroupId);
            return Ok(plans);
        }

        [HttpGet("{PlanId}/Share")]
        public async Task<IActionResult> GetShareToStudyGroup([FromRoute(Name = "StudyGroupId")] string studyGroupId, [FromRoute(Name = "PlanId")] string planId)
        {
            var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized("User is not authenticated.");
            var isMember = await _groups.IsUserMemberAsync(studyGroupId, userId);
            if (!isMember) return Forbid();

            if (!Guid.TryParse(studyGroupId, out var gid)) return BadRequest("Invalid studyGroupId.");
            if (!Guid.TryParse(planId, out var planGuid)) return BadRequest("Invalid planId.");

            var stableId = await ResolveStableIdAsync(planGuid);
            if (stableId == Guid.Empty) return NotFound();

            var rec = await _db.StudyGroupStudyPlans.AsNoTracking()
                .FirstOrDefaultAsync(x => x.StudyGroupId == gid && x.StudyPlanStableId == stableId);

            if (rec == null) return NotFound();
            return Ok(rec);
        }

        [HttpPost("{PlanId}/Share")]
        public async Task<IActionResult> ShareToStudyGroup([FromRoute(Name = "StudyGroupId")] string studyGroupId, [FromRoute(Name = "PlanId")] string planId, [FromBody] ShareStudyPlanRequest request)
        {
            var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized("User is not authenticated.");
            var isManager = await _groups.IsUserManagerAsync(studyGroupId, userId);
            if (!isManager) return Forbid();
            if (!Guid.TryParse(studyGroupId, out var groupGuid)) return BadRequest("Invalid studyGroupId.");
            if (!Guid.TryParse(planId, out var planGuid)) return BadRequest("Invalid planId.");
            if (!await _permissions.CanAdoptPlanToGroupAsync(userId, planGuid, groupGuid, HttpContext.RequestAborted))
            {
                return Forbid();
            }

            var rec = await _sharingService.ShareToStudyGroupAsync(studyGroupId, planId, request.Permission, request.AutoEnroll, request.VersionNumber, userId);
            return Ok(rec);
        }

        [HttpDelete("{PlanId}")]
        public async Task<IActionResult> UnshareFromStudyGroup([FromRoute(Name = "StudyGroupId")] string studyGroupId, [FromRoute(Name = "PlanId")] string planId)
        {
            var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized("User is not authenticated.");
            var isManager = await _groups.IsUserManagerAsync(studyGroupId, userId);
            if (!isManager) return Forbid();

            var ok = await _sharingService.UnshareFromStudyGroupAsync(studyGroupId, planId);
            return ok ? Ok(new { message = "Unshared" }) : NotFound();
        }
    }
}
