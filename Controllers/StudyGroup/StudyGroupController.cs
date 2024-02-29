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
    public async Task<ActionResult<StudyGroup>> GetMyStudyGroup()
    {
        // Retrieve the user's ID from the ClaimsPrincipal
        string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the user is authenticated
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("User is not authenticated.");
        }

        var group = await _studyGroupService.GetStudyGroupByUser(userId);
        if (group == null)
        {
            return NotFound("Study group not found.");
        }
        return Ok(group);
    }

    [HttpPost("CreateStudyGroup")]
    public async Task<ActionResult<StudyGroup>> CreateStudyGroupAsync([FromBody] StudyGroupDTO studyGroupDTO)
    {
        // Logic to create a new group
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
            return Ok(); // Plan saved successfully
        }
        else
        {
            return BadRequest("同名学习小组已经存在。");
        }
    }

    [HttpDelete("DeleteStudyGroup/{groupId}")]
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
}