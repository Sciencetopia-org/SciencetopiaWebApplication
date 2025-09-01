using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sciencetopia.Services;
using Sciencetopia.Models;

namespace Sciencetopia.Controllers.StudyGroups;

[ApiController]
[Route("api/[controller]")]
public class StudyGroupController : ControllerBase
{
    // Dependency injection for database context
    private readonly StudyGroupService _studyGroupService;

    public StudyGroupController(StudyGroupService studyGroupService)
    {
        _studyGroupService = studyGroupService;
    }

    [HttpGet("GetAllStudyGroups")]
    public async Task<ActionResult<IEnumerable<StudyGroup>>> GetAllStudyGroups()
    {
        var groups = await _studyGroupService.GetAllStudyGroups();
        return Ok(groups);
    }

    [HttpGet("GetStudyGroupById/{groupId}")]
    public async Task<ActionResult<StudyGroup>> GetStudyGroupById(string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        var group = await _studyGroupService.GetStudyGroupByIdAsync(groupId);
        if (group == null)
        {
            return NotFound("Study group not found.");
        }
        return Ok(group);
    }

    [HttpGet("GetUserRoleInGroup/{groupId}")]
    [Authorize] // This endpoint requires user authorization
    public async Task<ActionResult<string>> GetUserRoleInGroup(string groupId)
    {
        // Retrieve the user's ID from the ClaimsPrincipal
        string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the user is authenticated
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("User is not authenticated.");
        }

        string userRole = await _studyGroupService.GetUserRoleInGroupAsync(groupId, userId);
        if (userRole == null)
        {
            return NotFound("User role not found.");
        }

        return Ok(userRole);
    }

    [HttpGet("GetStudyGroupMembers/{groupId}")]
    public async Task<ActionResult<IEnumerable<GroupMember>>> GetStudyGroupMembers(string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        var members = await _studyGroupService.GetStudyGroupMembers(groupId);
        if (members == null)
        {
            return NotFound("Study group not found.");
        }
        return Ok(members);
    }

    // [HttpGet("GetStudyGroupByUser/{userId}")]
    // public async Task<ActionResult<StudyGroup>> GetStudyGroupByUser(string userId)
    // {
    //     var group = await _studyGroupService.GetStudyGroupByUser(userId);
    //     if (group == null)
    //     {
    //         return NotFound("Study group not found.");
    //     }
    //     return Ok(group);
    // }

    [HttpGet("GetGroupManagers/{studyGroupId}")]
    public async Task<IActionResult> GetGroupManagers(string studyGroupId)
    {
        if (!Guid.TryParse(studyGroupId, out _))
        {
            return BadRequest("Invalid studyGroupId format. Expected GUID.");
        }
        var managerIds = await _studyGroupService.GetGroupManagersAsync(studyGroupId);
        if (managerIds == null || !managerIds.Any())
        {
            return NotFound("No managers found for this study group.");
        }
        return Ok(managerIds);
    }


    [HttpGet("GetStudyGroup")]
    [Authorize] // Ensure only authenticated users can access this endpoint
    public async Task<ActionResult<List<StudyGroup>>> GetStudyGroup([FromQuery] string? targetUserId = null)
    {
        // Retrieve the authenticated user's ID from the ClaimsPrincipal
        string currentUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the user is authenticated
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("User is not authenticated.");
        }

        // If targetUserId is provided, use it; otherwise, use the authenticated user's ID
        string userIdToFetch = !string.IsNullOrEmpty(targetUserId) ? targetUserId : currentUserId;

        // Fetch the study groups based on the provided or authenticated userId
        var groups = await _studyGroupService.GetStudyGroupByUser(userIdToFetch, currentUserId);

        // Return an empty list if no groups are found, instead of returning NotFound
        if (groups == null || !groups.Any())
        {
            return Ok(new List<StudyGroup>()); // Return an empty list if no study groups are found
        }

        return Ok(groups); // Return the found groups
    }

    [HttpPost("CreateStudyGroup")]
    [Authorize] // Ensure only authenticated users can access this endpoint
    public async Task<ActionResult> CreateStudyGroupAsync([FromBody] StudyGroupDTO studyGroupDTO)
    {
        // Retrieve the user's ID from the ClaimsPrincipal
        string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the user is authenticated
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("User is not authenticated.");
        }

        var result = await _studyGroupService.CreateStudyGroupAsync(studyGroupDTO, userId);
        if (result)
        {
            return Ok("创建学习小组的申请已经成功提交审核！");
        }
        else
        {
            return BadRequest("创建学习小组的申请失败！请检查小组名称是否已存在或其他错误。");
        }
    }

    [HttpPost("ApproveStudyGroup")]
    [Authorize(Roles = "administrator")]
    public async Task<ActionResult> ApproveStudyGroupAsync([FromBody] string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        // Retrieve the admin's ID from the ClaimsPrincipal
        string adminUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the admin is authenticated
        if (string.IsNullOrEmpty(adminUserId))
        {
            return Unauthorized("Admin is not authenticated.");
        }

        var result = await _studyGroupService.ApproveStudyGroupAsync(groupId);
        if (result)
        {
            return Ok("Study group has been approved successfully.");
        }
        else
        {
            return BadRequest("Failed to approve the study group.");
        }
    }

    [HttpPost("RejectStudyGroup")]
    [Authorize(Roles = "administrator")]
    public async Task<ActionResult> RejectStudyGroupAsync([FromBody] string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        // Retrieve the admin's ID from the ClaimsPrincipal
        string adminUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the admin is authenticated
        if (string.IsNullOrEmpty(adminUserId))
        {
            return Unauthorized("Admin is not authenticated.");
        }

        var result = await _studyGroupService.RejectStudyGroupAsync(groupId);
        if (result)
        {
            return Ok("Study group has been rejected successfully.");
        }
        else
        {
            return BadRequest("Failed to reject the study group.");
        }
    }

    [HttpGet("ViewCreateStudyGroupRequests")]
    [Authorize(Roles = "administrator")]
    public async Task<ActionResult> ViewCreateStudyGroupRequests()
    {
        // Retrieve the admin's ID from the ClaimsPrincipal
        string adminUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the admin is authenticated
        if (string.IsNullOrEmpty(adminUserId))
        {
            return Unauthorized("Admin is not authenticated.");
        }

        var requests = await _studyGroupService.ViewCreateStudyGroupRequestsAsync();
        if (requests == null || !requests.Any())
        {
            return NotFound("No pending study group requests found.");
        }
        return Ok(requests);
    }

    [HttpDelete("DeleteStudyGroup/{groupId}")]
    [Authorize] // Ensure only authenticated users can access this endpoint
    public async Task<IActionResult> DeleteStudyGroup(string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        // Retrieve the user's ID from the ClaimsPrincipal
        string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the user is authenticated
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("User is not authenticated.");
        }

        var result = await _studyGroupService.DeleteStudyGroupAsync(groupId, userId);
        if (result)
        {
            return Ok("Study group deleted successfully.");
        }
        else
        {
            return BadRequest("Error deleting study group or permission denied.");
        }
    }

    [HttpPost("ApplyToJoin")]
    [Authorize] // Ensure only authenticated users can apply
    public async Task<IActionResult> ApplyToJoin([FromBody] ApplyToJoinRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Retrieve the user's ID from the ClaimsPrincipal
        string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the user is authenticated
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("User is not authenticated.");
        }

        if (string.IsNullOrEmpty(request.StudyGroupId))
        {
            return BadRequest("Study group ID is required.");
        }

        var result = await _studyGroupService.ApplyToJoin(userId, request.StudyGroupId);
        if (result)
        {
            return Ok("Application submitted successfully.");
        }
        else
        {
            return BadRequest("Failed to submit application. The group may not exist, or you may have already applied.");
        }
    }

    [HttpPost("LeaveStudyGroup")]
    public async Task<IActionResult> LeaveStudyGroup([FromBody] LeaveGroupRequest leaveGroupRequest)
    {
        try
        {
            var result = await _studyGroupService.LeaveStudyGroup(leaveGroupRequest.UserId, leaveGroupRequest.GroupId);

            if (result)
            {
                return Ok(new { message = "Successfully left the study group." });
            }
            else
            {
                return NotFound(new { message = "User was not a member of the study group." });
            }
        }
        catch (InvalidOperationException ex)
        {
            // Return a 400 Bad Request if the user is the manager and cannot leave
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpPost("DissolveStudyGroup")]
    public async Task<IActionResult> DissolveStudyGroup([FromBody] DissolveGroupRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.UserId))
            {
                return BadRequest("User ID is required.");
            }

            if (string.IsNullOrEmpty(request.GroupId))
            {
                return BadRequest("Group ID is required.");
            }

            var result = await _studyGroupService.DissolveStudyGroup(request.UserId, request.GroupId);

            return Ok(new { message = "Study group successfully dissolved." });
        }
        catch (UnauthorizedAccessException ex)
        {
            // Return a 403 Forbidden with a custom message
            return StatusCode(403, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Internal server error: {ex.Message}" });
        }
    }

    [HttpPost("UpdateApplicationStatus")]
    public async Task<IActionResult> UpdateApplicationStatus([FromBody] UpdateStatusRequest request)
    {
        if (string.IsNullOrEmpty(request.UserId) || string.IsNullOrEmpty(request.StudyGroupId) || request.Status == null)
        {
            return BadRequest("User ID, Study Group ID, and Status are required.");
        }

        var result = await _studyGroupService.UpdateApplicationStatusAsync(request.UserId, request.StudyGroupId, request.Status);
        if (result)
        {
            return Ok("Application status updated successfully.");
        }
        else
        {
            return BadRequest("Could not update the application status.");
        }
    }

    [HttpPost("JoinGroup/{groupId}")]
    public async Task<IActionResult> JoinGroup(string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        // Retrieve the user's ID from the ClaimsPrincipal
        string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the user is authenticated
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("User is not authenticated.");
        }

        var result = await _studyGroupService.JoinGroupAsync(groupId, userId);
        if (result)
        {
            return Ok("Joined group successfully.");
        }
        else
        {
            return BadRequest("Error joining group or permission denied.");
        }
    }

    // Other endpoints like JoinGroup, PostUpdate, etc.
    [HttpGet("GetJoinRequests/{groupId}")]
    public async Task<ActionResult<IEnumerable<JoinRequest>>> GetJoinRequests(string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        var requests = await _studyGroupService.GetJoinRequests(groupId);
        if (requests == null)
        {
            return NotFound("Join requests not found.");
        }
        return Ok(requests);
    }

    // API to get the count of pending join requests
    [HttpGet("GetPendingJoinRequestsCount/{groupId}")]
    public async Task<IActionResult> GetPendingJoinRequestsCount(string groupId)
    {
        try
        {
            if (!Guid.TryParse(groupId, out _))
            {
                return BadRequest("Invalid groupId format. Expected GUID.");
            }
            // Call the service to get the count of pending join requests
            var pendingCount = await _studyGroupService.GetPendingJoinRequestsCount(groupId);

            return Ok(pendingCount);  // Return the count as the response
        }
        catch (Exception ex)
        {
            // Handle exceptions and return appropriate error response
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpGet("GetActivityLogs/{groupId}")]
    public async Task<ActionResult<IEnumerable<ActivityLog>>> GetActivityLogs(string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        var logs = await _studyGroupService.GetActivityLogs(groupId);
        if (logs == null)
        {
            return NotFound("Activity logs not found.");
        }
        return Ok(logs);
    }
}
