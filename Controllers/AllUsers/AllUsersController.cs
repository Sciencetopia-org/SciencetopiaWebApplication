using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Models;
using Sciencetopia.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

[Route("api/AllUsers/[controller]")]
[ApiController]
public class AllUsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly BlobServiceClient _blobServiceClient; // Blob service client

    private readonly UserService _userService;

    public AllUsersController(UserManager<ApplicationUser> userManager, BlobServiceClient blobServiceClient, UserService userService)
    {
        _userManager = userManager;
        _blobServiceClient = blobServiceClient; // Initialize blob service client
        _userService = userService;
    }

    [HttpGet("GetUserInfoById/{userId}")]
    public async Task<IActionResult> GetUserInfoById(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var userInfo = new UserInformationDTO
        {
            UserName = user.UserName,
            Email = user.Email,
            SelfIntroduction = user.SelfIntroduction,
        };

        return Ok(userInfo);
    }

    [HttpGet("GetUserAvatarById/{userId}")]
    public async Task<IActionResult> GetUserAvatarById(string userId)
    {
        // Use the extracted method to fetch the avatar URL
        var avatarUrl = await _userService.FetchUserAvatarUrlByIdAsync(userId);

        if (string.IsNullOrEmpty(avatarUrl))
            {
                // Handle cases where no avatar is set, if necessary
                return Ok(new { AvatarUrl = string.Empty });
            }

            // Return the avatar URL
            return Ok(new { AvatarUrl = avatarUrl });
    }
}