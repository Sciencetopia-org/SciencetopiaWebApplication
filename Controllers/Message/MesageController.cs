using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Services;
using Sciencetopia.Services.Messaging;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Sciencetopia.Services.ContentSafety;

namespace Sciencetopia.Controllers.Messaging;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessageController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly UserService _userService;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly MessageAttachmentService _attachmentService;
    private readonly IContentModerationService _contentModeration;

    public MessageController(ApplicationDbContext context, UserService userService, BlobServiceClient blobServiceClient, MessageAttachmentService attachmentService, IContentModerationService contentModeration)
    {
        _context = context;
        _userService = userService;
        _blobServiceClient = blobServiceClient;
        _attachmentService = attachmentService;
        _contentModeration = contentModeration;
    }
    [HttpPost("SendMessage")]
    public async Task<ActionResult<MessageWithUserDetailsDTO>> PostMessage([FromBody] SendMessageRequest request)
    {
        var currentUserId = CurrentUserId();
        if (currentUserId == null) return Unauthorized();

        if (!Guid.TryParse(request.ConversationId, out var conversationGuid))
        {
            return BadRequest("Invalid conversationId format.");
        }

        if (string.IsNullOrWhiteSpace(request.ReceiverId))
        {
            return BadRequest("ReceiverId is required.");
        }

        var moderation = await _contentModeration.ReviewTextAsync(new[] { request.Content }, HttpContext.RequestAborted);
        if (!moderation.Allowed)
        {
            return BadRequest(new
            {
                message = "内容未通过审核，请修改后再发布。",
                reason = moderation.Reason,
                blockedCategories = moderation.BlockedCategories
            });
        }

        var conversationHasMessages = await _context.Messages
            .AnyAsync(m => m.ConversationId == conversationGuid);
        var conversationExists = conversationHasMessages || await _context.Conversations
            .AsNoTracking()
            .AnyAsync(c => c.Id == conversationGuid);

        if (conversationHasMessages && !await IsConversationParticipantAsync(conversationGuid, currentUserId))
        {
            return Forbid();
        }

        if (!conversationExists)
        {
            _context.Conversations.Add(new Conversation { Id = conversationGuid });
        }

        var message = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationGuid,
            SenderId = currentUserId,
            ReceiverId = request.ReceiverId,
            Content = _attachmentService.NormalizeForStorage(request.Content ?? string.Empty),
            SentTime = DateTimeOffset.UtcNow,
            IsRead = false
        };

        _context.Messages.Add(message);
        await _context.SaveChangesAsync();

        return Ok(new MessageWithUserDetailsDTO
        {
            Id = message.Id,
            Content = _attachmentService.GetClientReadableContent(message.Content),
            SentTime = message.SentTime,
            Sender = new UserDetailsDTO
            {
                Id = currentUserId,
                UserName = await _userService.GetUserNameByIdAsync(currentUserId),
                AvatarUrl = await _userService.FetchUserAvatarUrlByIdAsync(currentUserId)
            },
            IsRead = false
        });
    }

    [HttpGet("GetMessages")]
    [Authorize(Roles = "administrator")]
    public async Task<ActionResult<IEnumerable<Message>>> GetMessages()
    {
        return await _context.Messages.ToListAsync();
    }

    [HttpGet("GetGroupedMessagesByUser/{userId}")]
    public async Task<ActionResult<IEnumerable<GroupedMessageDTO>>> GetGroupedMessagesByUser(string userId)
    {
        var currentUserId = CurrentUserId();
        if (currentUserId == null) return Unauthorized();
        if (!string.Equals(userId, currentUserId, StringComparison.OrdinalIgnoreCase) && !User.IsInRole("administrator"))
        {
            return Forbid();
        }

        // Step 1: Fetch the conversations with sorted messages
        var groupedMessages = await _context.Messages
            .Include(m => m.Sender)
            .Include(m => m.Receiver)
            .Where(m => m.SenderId == userId || m.ReceiverId == userId)
            .GroupBy(m => m.ConversationId)
            .Select(group => new
            {
                ConversationId = group.Key,
                PartnerId = group.Select(m => m.SenderId == userId ? m.ReceiverId : m.SenderId).FirstOrDefault(),
                PartnerName = group.Select(m => m.SenderId == userId ? m.Receiver.UserName : m.Sender.UserName).FirstOrDefault(),
                UnreadMessageCount = group.Count(m => !m.IsRead && m.ReceiverId == userId),
                Messages = group.OrderBy(m => m.SentTime)
                                .Select(m => new MessageWithUserDetailsDTO
                                {
                                    Id = m.Id,
                                    Content = m.Content,
                                    SentTime = m.SentTime,
                                    Sender = new UserDetailsDTO
                                    {
                                        Id = m.Sender.Id,
                                        UserName = m.Sender.UserName,
                                    },
                                    IsRead = m.IsRead
                                }).ToList(),
                LastMessageSentTime = group.Max(m => m.SentTime) // Fetch the latest sent time for sorting
            })
            .OrderByDescending(group => group.LastMessageSentTime) // Sort by the latest sent time
            .ToListAsync();

        // Step 2: Transform the anonymous type to GroupedMessageDTO and fetch avatar URLs
        var result = new List<GroupedMessageDTO>();
        foreach (var group in groupedMessages)
        {
            var groupedMessageDto = new GroupedMessageDTO
            {
                ConversationId = group.ConversationId,
                PartnerId = group.PartnerId,
                PartnerName = group.PartnerName,
                UnreadMessageCount = group.UnreadMessageCount,
                Messages = group.Messages,
            };

            if (!string.IsNullOrEmpty(group.PartnerId))
            {
                groupedMessageDto.PartnerAvatarUrl = await _userService.FetchUserAvatarUrlByIdAsync(group.PartnerId);
            }

            foreach (var message in groupedMessageDto.Messages)
            {
                message.Content = _attachmentService.GetClientReadableContent(message.Content);
                if (message.Sender != null && !string.IsNullOrEmpty(message.Sender.Id))
                {
                    var avatarUrl = await _userService.FetchUserAvatarUrlByIdAsync(message.Sender.Id);
                    message.Sender.AvatarUrl = avatarUrl;
                }
            }

            result.Add(groupedMessageDto);
        }

        return Ok(result);
    }

    [HttpGet("GetConversation/{conversationId}")]
    public async Task<ActionResult<GroupedMessageDTO>> GetConversation(string conversationId)
    {
        var currentUserId = CurrentUserId();
        if (currentUserId == null) return Unauthorized();

        if (!Guid.TryParse(conversationId, out Guid conversationGuid))
        {
            return BadRequest("Invalid conversationId format.");
        }

        if (!await IsConversationParticipantAsync(conversationGuid, currentUserId) && !User.IsInRole("administrator"))
        {
            return Forbid();
        }

        var conversationGroup = await _context.Messages
            .Include(m => m.Sender)
            .Include(m => m.Receiver)
            .Where(m => m.ConversationId == conversationGuid)
            .OrderBy(m => m.SentTime)
            .ToListAsync();

        // Step 2: If no messages are found, return an empty conversation structure
        if (!conversationGroup.Any())
        {
            return Ok(new GroupedMessageDTO
            {
                ConversationId = conversationGuid,
                PartnerId = null,
                PartnerName = "Unknown", // Placeholder, adjust if you have specific data requirements
                UnreadMessageCount = 0,
                Messages = new List<MessageWithUserDetailsDTO>()
            });
        }

        // Step 3: Identify the partner user (assuming it's either the sender or receiver)
        var partnerMessage = conversationGroup.FirstOrDefault();
        var partnerId = partnerMessage.SenderId == conversationGroup.First().ReceiverId
            ? partnerMessage.SenderId
            : partnerMessage.ReceiverId;

        // Fetch partner details
        var partnerName = partnerId != null ? await _userService.GetUserNameByIdAsync(partnerId) : "Unknown";
        var partnerAvatarUrl = !string.IsNullOrEmpty(partnerId)
            ? await _userService.FetchUserAvatarUrlByIdAsync(partnerId)
            : null;

        // Step 4: Populate the DTO with message details
        var conversationDto = new GroupedMessageDTO
        {
            ConversationId = conversationGuid,
            PartnerId = partnerId,
            PartnerName = partnerName,
            PartnerAvatarUrl = partnerAvatarUrl,
            UnreadMessageCount = conversationGroup.Count(m => !m.IsRead && m.ReceiverId == partnerMessage.ReceiverId),
            Messages = conversationGroup.Select(m => new MessageWithUserDetailsDTO
            {
                Id = m.Id,
                Content = _attachmentService.GetClientReadableContent(m.Content),
                SentTime = m.SentTime,
                Sender = new UserDetailsDTO
                {
                    Id = m.Sender.Id,
                    UserName = m.Sender.UserName,
                    AvatarUrl = m.Sender.AvatarUrl
                },
                IsRead = m.IsRead
            }).ToList()
        };

        return Ok(conversationDto);
    }

    [HttpPost("MarkAsRead")]
    public async Task<IActionResult> MarkAsRead([FromBody] MarkAsReadRequest request)
    {
        var currentUserId = CurrentUserId();
        if (currentUserId == null) return Unauthorized();

        if (!Guid.TryParse(request.ConversationId, out Guid conversationGuid))
        {
            return BadRequest("Invalid ConversationId format.");
        }

        if (!await IsConversationParticipantAsync(conversationGuid, currentUserId))
        {
            return Forbid();
        }

        var messages = await _context.Messages
            .Where(m => m.ConversationId == conversationGuid && m.ReceiverId == currentUserId && !m.IsRead)
            .ToListAsync();

        foreach (var message in messages)
        {
            message.IsRead = true;
        }

        await _context.SaveChangesAsync();

        return Ok(new { Message = "Messages marked as read" });
    }

    [HttpPost("CreateAttachmentUpload")]
    [Authorize]
    public async Task<IActionResult> CreateAttachmentUpload([FromBody] AttachmentUploadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ContentType) ||
            !request.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Only image uploads are supported.");
        }

        var container = _blobServiceClient.GetBlobContainerClient("message-attachments");
        await container.CreateIfNotExistsAsync();

        var ext = Path.GetExtension(request.FileName);
        if (string.IsNullOrWhiteSpace(ext) || ext.Length > 10)
        {
            ext = ".jpg";
        }

        var name = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var blob = container.GetBlobClient(name);
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            Resource = "b",
            StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresOn = DateTimeOffset.UtcNow.AddMinutes(15),
            ContentType = request.ContentType
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write);

        var storedContent = _attachmentService.NormalizeForStorage(blob.Uri.ToString());
        var readUrl = _attachmentService.GetClientReadableContent(storedContent, TimeSpan.FromMinutes(30));
        return Ok(new
        {
            uploadUrl = blob.GenerateSasUri(sasBuilder).ToString(),
            blobUrl = blob.Uri.ToString(),
            readUrl,
            headers = new Dictionary<string, string>
            {
                ["x-ms-blob-type"] = "BlockBlob",
                ["Content-Type"] = request.ContentType
            }
        });
    }

    private string? CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private Task<bool> IsConversationParticipantAsync(Guid conversationId, string userId)
    {
        return _context.Messages.AsNoTracking()
            .AnyAsync(m => m.ConversationId == conversationId && (m.SenderId == userId || m.ReceiverId == userId));
    }

}

public record AttachmentUploadRequest(string? FileName, string ContentType);
public record SendMessageRequest(string ConversationId, string ReceiverId, string? Content);
