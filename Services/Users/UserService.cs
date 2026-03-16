using System;
using System.Linq;
using Neo4j.Driver;
using Sciencetopia.Models;
using Microsoft.AspNetCore.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.EntityFrameworkCore;

public class UserService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly BlobServiceClient _blobServiceClient;

    // Constructor injection for dependencies
    public UserService(UserManager<ApplicationUser> userManager, BlobServiceClient blobServiceClient)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _blobServiceClient = blobServiceClient ?? throw new ArgumentNullException(nameof(blobServiceClient));
    }

    // Fetch user information by user ID
    public async Task<string> FetchUserAvatarUrlByIdAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || string.IsNullOrEmpty(user.AvatarUrl))
        {
            // Return a default avatar URL or an empty string if no avatar is set
            // Example: return "path/to/default/avatar.jpg";
            return string.Empty;
        }

        // Generate and return the SAS URL for the user's avatar
        var avatarSasUrl = GenerateBlobSasUri(_blobServiceClient, "avatars", $"{userId}.jpg");
        return avatarSasUrl;
    }

    public async Task<UserInformationDTO?> GetUserInfoByIdAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return null; // Return null if the user is not found
        }

        var userInfo = new UserInformationDTO
        {
            UserName = user.UserName,
            Email = user.Email,
            SelfIntroduction = user.SelfIntroduction,
        };

        return userInfo;
    }

    // New method to fetch only the UserName
    public async Task<string> GetUserNameByIdAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        return user?.UserName ?? string.Empty; // Return the UserName if user exists, otherwise an empty string
    }

    public async Task<Dictionary<string, UserDisplayInfo>> GetUserDisplayInfoByIdsAsync(IEnumerable<string> userIds)
    {
        var ids = userIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? new List<string>();

        var result = ids.ToDictionary(
            id => id,
            _ => new UserDisplayInfo(string.Empty, string.Empty),
            StringComparer.Ordinal);

        if (ids.Count == 0)
        {
            return result;
        }

        var users = await _userManager.Users
            .AsNoTracking()
            .Where(user => ids.Contains(EF.Functions.Collate(user.Id, "SQL_Latin1_General_CP1_CI_AS")))
            .Select(user => new
            {
                user.Id,
                user.UserName,
                user.AvatarUrl
            })
            .ToListAsync();

        foreach (var user in users)
        {
            var avatarUrl = string.IsNullOrEmpty(user.AvatarUrl)
                ? string.Empty
                : GenerateBlobSasUri(_blobServiceClient, "avatars", $"{user.Id}.jpg");

            result[user.Id] = new UserDisplayInfo(user.UserName ?? string.Empty, avatarUrl);
        }

        return result;
    }

    private string GenerateBlobSasUri(BlobServiceClient blobServiceClient, string containerName, string blobName, TimeSpan? lifetime = null)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        var sasLifetime = lifetime ?? TimeSpan.FromDays(30);

        var sasBuilder = new BlobSasBuilder()
        {
            BlobContainerName = containerClient.Name,
            BlobName = blobClient.Name,
            Resource = "b", // b for blob
            StartsOn = DateTimeOffset.UtcNow,
            ExpiresOn = DateTimeOffset.UtcNow.Add(sasLifetime)
        };

        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sasToken = blobClient.GenerateSasUri(sasBuilder).Query;

        return blobClient.Uri + sasToken;
    }
}

public record UserDisplayInfo(string UserName, string AvatarUrl);
