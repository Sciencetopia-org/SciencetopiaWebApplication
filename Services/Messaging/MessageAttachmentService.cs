using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using System.Linq;

namespace Sciencetopia.Services.Messaging;

public class MessageAttachmentService
{
    private readonly BlobServiceClient _blobServiceClient;

    public MessageAttachmentService(BlobServiceClient blobServiceClient)
    {
        _blobServiceClient = blobServiceClient;
    }

    public string NormalizeForStorage(string content)
    {
        if (!TryGetAttachmentBlobClient(content, out var blobClient))
        {
            return content;
        }

        return blobClient.Uri.ToString();
    }

    public string GetClientReadableContent(string content, TimeSpan? lifetime = null)
    {
        if (!TryGetAttachmentBlobClient(content, out var blobClient))
        {
            return content;
        }

        var sasLifetime = lifetime ?? TimeSpan.FromDays(30);
        return GenerateReadSas(blobClient, sasLifetime).ToString();
    }

    private bool TryGetAttachmentBlobClient(string? content, out BlobClient blobClient)
    {
        blobClient = null!;

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (!Uri.TryCreate(content, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        if (!string.Equals(segments[0], "message-attachments", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var blobName = string.Join('/', segments.Skip(1));
        var containerClient = _blobServiceClient.GetBlobContainerClient("message-attachments");
        blobClient = containerClient.GetBlobClient(blobName);
        return true;
    }

    private static Uri GenerateReadSas(BlobClient blobClient, TimeSpan lifetime)
    {
        if (!blobClient.CanGenerateSasUri)
        {
            throw new InvalidOperationException("Blob SAS generation is not configured for the current credentials.");
        }

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name,
            Resource = "b",
            StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresOn = DateTimeOffset.UtcNow.Add(lifetime)
        };

        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        return blobClient.GenerateSasUri(sasBuilder);
    }
}
