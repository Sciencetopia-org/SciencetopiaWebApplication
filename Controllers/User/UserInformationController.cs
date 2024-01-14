using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Models;
using Sciencetopia.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

[Route("api/users/[controller]")]
[ApiController]
[Authorize] // Ensures only authenticated users can access methods in this controller
public class UserInformationController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly BlobServiceClient _blobServiceClient; // Blob service client

    public UserInformationController(UserManager<ApplicationUser> userManager, BlobServiceClient blobServiceClient)
    {
        _userManager = userManager;
        _blobServiceClient = blobServiceClient; // Initialize blob service client
    }

    [HttpGet("GetUserInfo")]
    public async Task<IActionResult> GetUserInfo()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier); // Get user ID from the claim
        if (userId == null)
        {
            return Unauthorized();
        }

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
            Gender = user.Gender,
            Birth = user.BirthDate,
            PhoneNumber = user.PhoneNumber
        };

        return Ok(userInfo);
    }

    [HttpPut("Update")]
    public async Task<IActionResult> UpdateUserInformation([FromBody] UserUpdateDTO model)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier); // Get user ID from the token
        var user = await _userManager.FindByIdAsync(userId);

        if (user != null)
        {
            // Update user properties
            user.SelfIntroduction = model.SelfIntroduction;
            user.Gender = model.Gender;
            user.BirthDate = new DateTime(model.BirthDate.Year, model.BirthDate.Month, model.BirthDate.Day);

            // Update user in the database
            var result = await _userManager.UpdateAsync(user);

            if (result.Succeeded)
            {
                return Ok(new { message = "User information updated successfully." });
            }

            // Handle errors
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        return BadRequest(ModelState);
    }

    [HttpPost("ChangeUsername")]
    public async Task<IActionResult> ChangeUsername([FromBody] ChangeUsernameDTO model)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return NotFound("User not found.");

        if (user.LastUsernameChangeDate.HasValue &&
            (DateTime.Now - user.LastUsernameChangeDate.Value).TotalDays < 90)
        {
            return BadRequest("Username can only be changed once every three months.");
        }

        var setUsernameResult = await _userManager.SetUserNameAsync(user, model.NewUsername);
        if (!setUsernameResult.Succeeded)
        {
            return BadRequest(setUsernameResult.Errors);
        }

        user.LastUsernameChangeDate = DateTime.Now;
        await _userManager.UpdateAsync(user);

        return Ok("Username updated successfully.");
    }

    [HttpPost("UploadAvatar")]
    public async Task<IActionResult> UploadAvatar(IFormFile avatarFile)
    {
        if (avatarFile == null || avatarFile.Length == 0)
        {
            return BadRequest("Invalid file.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        try
        {
            var blobContainer = _blobServiceClient.GetBlobContainerClient("avatars");
            await blobContainer.CreateIfNotExistsAsync();

            var blobClient = blobContainer.GetBlobClient($"{userId}.jpg");

            await using var stream = avatarFile.OpenReadStream();
            await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = avatarFile.ContentType });

            user.AvatarUrl = blobClient.Uri.ToString();
            var result = await _userManager.UpdateAsync(user);

            if (result.Succeeded)
            {
                return Ok(new { AvatarUrl = user.AvatarUrl });
            } 

            return BadRequest("Failed to update user avatar URL.");
        }
        catch (Exception)
        {
            // Log the exception details
            // _logger.LogError(ex, "Error uploading avatar for user {UserId}.", userId);
            return StatusCode(500, "An error occurred while uploading the avatar.");
        }
    }

}
