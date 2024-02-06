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