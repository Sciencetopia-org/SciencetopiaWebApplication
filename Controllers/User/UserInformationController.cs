using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Models;
using Sciencetopia.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Sciencetopia.Services.ContentSafety;

namespace Sciencetopia.Controllers.Users;

[Route("api/users/[controller]")]
[ApiController]
[Authorize] // Ensures only authenticated users can access methods in this controller
public class UserInformationController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly BlobServiceClient _blobServiceClient; // Blob service client
    private readonly IContentModerationService _contentModeration;

    public UserInformationController(UserManager<ApplicationUser> userManager, BlobServiceClient blobServiceClient, IContentModerationService contentModeration)
    {
        _userManager = userManager;
        _blobServiceClient = blobServiceClient; // Initialize blob service client
        _contentModeration = contentModeration;
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
            PhoneNumber = user.PhoneNumber,
            ShowStudyPlansPublicly = user.ShowStudyPlansPublicly,
            ShowStudyGroupsPublicly = user.ShowStudyGroupsPublicly,
        };

        return Ok(userInfo);
    }

    [HttpPut("Update")]
    public async Task<IActionResult> UpdateUserInformation([FromBody] UserUpdateDTO model)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier); // Get user ID from the token
        var user = await _userManager.FindByIdAsync(userId ?? string.Empty);

        if (user != null)
        {
            var moderation = await _contentModeration.ReviewTextAsync(new[] { model.SelfIntroduction }, HttpContext.RequestAborted);
            if (!moderation.Allowed)
            {
                return BadRequest(new
                {
                    message = "内容未通过审核，请修改后再发布。",
                    reason = moderation.Reason,
                    blockedCategories = moderation.BlockedCategories
                });
            }

            // Update user properties
            user.SelfIntroduction = model.SelfIntroduction;
            user.Gender = model.Gender;
            user.BirthDate = new DateTime(model.BirthDate.Year, model.BirthDate.Month, model.BirthDate.Day);
            user.ShowStudyPlansPublicly = model.ShowStudyPlansPublicly;
            user.ShowStudyGroupsPublicly = model.ShowStudyGroupsPublicly;

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

        var moderation = await _contentModeration.ReviewTextAsync(new[] { model.NewUsername }, HttpContext.RequestAborted);
        if (!moderation.Allowed)
        {
            return BadRequest(new
            {
                message = "内容未通过审核，请修改后再发布。",
                reason = moderation.Reason,
                blockedCategories = moderation.BlockedCategories
            });
        }

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

    [HttpPost("CreateAvatarUpload")]
    public async Task<IActionResult> CreateAvatarUpload([FromBody] AvatarUploadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ContentType) ||
            !request.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Only image uploads are supported.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
        {
            return BadRequest("User ID not found.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var blobContainer = _blobServiceClient.GetBlobContainerClient("avatars");
        await blobContainer.CreateIfNotExistsAsync();

        var extension = Path.GetExtension(request.FileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 10)
        {
            extension = ".jpg";
        }

        var blobName = $"{userId}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var blobClient = blobContainer.GetBlobClient(blobName);
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name,
            Resource = "b",
            StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(15),
            ContentType = request.ContentType
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write);

        return Ok(new
        {
            uploadUrl = blobClient.GenerateSasUri(sasBuilder).ToString(),
            blobUrl = blobClient.Uri.ToString(),
            headers = new Dictionary<string, string>
            {
                ["x-ms-blob-type"] = "BlockBlob",
                ["Content-Type"] = request.ContentType
            }
        });
    }

    [HttpPost("CompleteAvatarUpload")]
    public async Task<IActionResult> CompleteAvatarUpload([FromBody] CompleteAvatarUploadRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
        {
            return BadRequest("User ID not found.");
        }

        if (!Uri.TryCreate(request.BlobUrl, UriKind.Absolute, out var blobUri))
        {
            return BadRequest("Invalid blob URL.");
        }

        var segments = blobUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3 ||
            !string.Equals(segments[0], "avatars", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(segments[1], userId, StringComparison.Ordinal))
        {
            return BadRequest("Avatar URL does not match the current user.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var blobName = string.Join('/', segments.Skip(1));
        var blobContainer = _blobServiceClient.GetBlobContainerClient("avatars");
        var blobClient = blobContainer.GetBlobClient(blobName);

        if (!await blobClient.ExistsAsync())
        {
            return BadRequest("Uploaded avatar was not found in object storage.");
        }

        user.AvatarUrl = blobClient.Uri.ToString();
        var result = await _userManager.UpdateAsync(user);

        if (result.Succeeded)
        {
            return Ok(new { AvatarUrl = user.AvatarUrl });
        }

        return BadRequest("Failed to update user avatar URL.");
    }

}

public record AvatarUploadRequest(string? FileName, string ContentType);
public record CompleteAvatarUploadRequest(string BlobUrl);
