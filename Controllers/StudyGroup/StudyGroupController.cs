using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sciencetopia.Services;
using Sciencetopia.Models;

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
        var members = await _studyGroupService.GetStudyGroupMembers(groupId);
        if (members == null)
        {
            return NotFound("Study group not found.");
        }
        return Ok(members);
    }

    [HttpGet("GetStudyGroupByUser/{userId}")]
    public async Task<ActionResult<StudyGroup>> GetStudyGroupByUser(string userId)
    {
        var group = await _studyGroupService.GetStudyGroupByUser(userId);
        if (group == null)
        {
            return NotFound("Study group not found.");
        }
        return Ok(group);
    }

    [HttpGet("GetMyStudyGroup")]
    [Authorize] // Ensure only authenticated users can access this endpoint
    public async Task<ActionResult<List<StudyGroup>>> GetMyStudyGroup()
    {
        // Retrieve the user's ID from the ClaimsPrincipal
        string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the user is authenticated
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("User is not authenticated.");
        }

        var groups = await _studyGroupService.GetStudyGroupByUser(userId);
        if (groups == null || !groups.Any())
        {
            return NotFound("Study groups not found.");
        }
        return Ok(groups);
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
            return BadRequest("同名学习小组已经存在。");
        }
    }

    [HttpPost("ApproveStudyGroup")]
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult> ApproveStudyGroupAsync([FromBody] string groupId)
    {
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
    [Authorize(Roles = "Administrator")]
    public async Task<ActionResult> RejectStudyGroupAsync([FromBody] string groupId)
    {
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

    [HttpPost("ViewCreateStudyGroupRequests")]
    [Authorize(Roles = "Administrator")]
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
        var requests = await _studyGroupService.GetJoinRequests(groupId);
        if (requests == null)
        {
            return NotFound("Join requests not found.");
        }
        return Ok(requests);
    }

    [HttpGet("GetActivityLogs/{groupId}")]
    public async Task<ActionResult<IEnumerable<ActivityLog>>> GetActivityLogs(string groupId)
    {
        var logs = await _studyGroupService.GetActivityLogs(groupId);
        if (logs == null)
        {
            return NotFound("Activity logs not found.");
        }
        return Ok(logs);
    }
}