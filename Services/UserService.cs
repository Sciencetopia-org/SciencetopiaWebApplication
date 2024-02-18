using Neo4j.Driver;
using System.Linq;
using Sciencetopia.Models;
using Microsoft.AspNetCore.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;

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

    private string GenerateBlobSasUri(BlobServiceClient blobServiceClient, string containerName, string blobName, int validMinutes = 30)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        var sasBuilder = new BlobSasBuilder()
        {
            BlobContainerName = containerClient.Name,
            BlobName = blobClient.Name,
            Resource = "b", // b for blob
            StartsOn = DateTimeOffset.UtcNow,
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(validMinutes)
        };

        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sasToken = blobClient.GenerateSasUri(sasBuilder).Query;

        return blobClient.Uri + sasToken;
    }
}