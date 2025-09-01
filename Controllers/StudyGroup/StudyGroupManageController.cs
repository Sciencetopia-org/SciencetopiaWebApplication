using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Services;
using Sciencetopia.Models;
using Sciencetopia.Authorization;
using System.Threading.Tasks;

namespace Sciencetopia.Controllers.StudyGroups;

[ApiController]
[Route("api/[controller]")]
public class StudyGroupManageController : ControllerBase
{
    private readonly StudyGroupService _studyGroupService;

    public StudyGroupManageController(StudyGroupService studyGroupService)
    {
        _studyGroupService = studyGroupService;
    }

    [HttpPost("InviteMember/{studyGroupId}")]
    [ServiceFilter(typeof(GroupManagerAuthorizeAttribute))]
    public async Task<IActionResult> InviteMember(string studyGroupId, [FromBody] InviteMemberRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (string.IsNullOrEmpty(request.MemberId))
        {
            return BadRequest("MemberId cannot be null or empty.");
        }

        var result = await _studyGroupService.InviteMemberAsync(studyGroupId, request.MemberId);
        if (result)
        {
            return Ok("Member invited successfully.");
        }
        else
        {
            return BadRequest("Failed to invite member.");
        }
    }

    [HttpPost("ApproveJoinRequest/{studyGroupId}")]
    [ServiceFilter(typeof(GroupManagerAuthorizeAttribute))]
    public async Task<IActionResult> ApproveJoinRequest(string studyGroupId, [FromBody] ApproveJoinRequest request)
    {
        if (string.IsNullOrEmpty(request.UserId))
        {
            return BadRequest("UserId cannot be null or empty.");
        }

        var result = await _studyGroupService.ApproveJoinRequestAsync(studyGroupId, request.UserId);
        if (result)
        {
            return Ok("Join request approved successfully.");
        }
        else
        {
            return BadRequest("Failed to approve join request.");
        }
    }

    [HttpPost("DeleteMember/{studyGroupId}")]
    [ServiceFilter(typeof(GroupManagerAuthorizeAttribute))]
    public async Task<IActionResult> DeleteMember(string studyGroupId, [FromBody] DeleteMemberRequest request)
    {
        if (string.IsNullOrEmpty(request.MemberId))
        {
            return BadRequest("MemberId cannot be null or empty.");
        }

        var result = await _studyGroupService.DeleteMemberAsync(studyGroupId, request.MemberId);
        if (result)
        {
            return Ok("Member deleted successfully.");
        }
        else
        {
            return BadRequest("Failed to delete member.");
        }
    }

    [HttpPost("TransferManagerRole/{studyGroupId}")]
    [ServiceFilter(typeof(GroupManagerAuthorizeAttribute))]
    public async Task<IActionResult> TransferManagerRole(string studyGroupId, [FromBody] TransferManagerRoleRequest request)
    {
        if (string.IsNullOrEmpty(request.NewManagerId))
        {
            return BadRequest("NewManagerId cannot be null or empty.");
        }

        var result = await _studyGroupService.TransferManagerRoleAsync(studyGroupId, request.NewManagerId);
        if (result)
        {
            return Ok("Manager role transferred successfully.");
        }
        else
        {
            return BadRequest("Failed to transfer manager role.");
        }
    }

    [HttpPost("RenameGroup/{studyGroupId}")]
    [ServiceFilter(typeof(GroupManagerAuthorizeAttribute))]
    public async Task<IActionResult> RenameGroup(string studyGroupId, [FromBody] RenameGroupRequest request)
    {
        if (string.IsNullOrEmpty(request.NewName))
        {
            return BadRequest("NewName cannot be null or empty.");
        }

        var result = await _studyGroupService.RenameGroupAsync(studyGroupId, request.NewName);
        if (result)
        {
            return Ok("Group renamed successfully.");
        }
        else
        {
            return BadRequest("Failed to rename group.");
        }
    }

    [HttpPost("EditDescription/{studyGroupId}")]
    [ServiceFilter(typeof(GroupManagerAuthorizeAttribute))]
    public async Task<IActionResult> EditDescription(string studyGroupId, [FromBody] EditDescriptionRequest request)
    {
        if (string.IsNullOrEmpty(request.NewDescription))
        {
            return BadRequest("NewDescription cannot be null or empty.");
        }

        var result = await _studyGroupService.EditDescriptionAsync(studyGroupId, request.NewDescription);
        if (result)
        {
            return Ok("Description updated successfully.");
        }
        else
        {
            return BadRequest("Failed to update description.");
        }
    }

    [HttpPost("EditProfilePicture/{studyGroupId}")]
    [ServiceFilter(typeof(GroupManagerAuthorizeAttribute))]
    public async Task<IActionResult> EditProfilePicture(string studyGroupId, [FromBody] EditProfilePictureRequest request)
    {
        if (string.IsNullOrEmpty(request.NewProfilePictureUrl))
        {
            return BadRequest("NewProfilePictureUrl cannot be null or empty.");
        }

        var result = await _studyGroupService.EditProfilePictureAsync(studyGroupId, request.NewProfilePictureUrl);
        if (result)
        {
            return Ok("Profile picture updated successfully.");
        }
        else
        {
            return BadRequest("Failed to update profile picture.");
        }
    }
}
