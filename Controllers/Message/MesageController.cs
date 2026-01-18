using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Services;
using Sciencetopia.Services.Messaging;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Authorization;

namespace Sciencetopia.Controllers.Messaging;

[ApiController]
[Route("api/[controller]")]
public class MessageController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly UserService _userService;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly MessageAttachmentService _attachmentService;

    public MessageController(ApplicationDbContext context, UserService userService, BlobServiceClient blobServiceClient, MessageAttachmentService attachmentService)
    {
        _context = context;
        _userService = userService;
        _blobServiceClient = blobServiceClient;
        _attachmentService = attachmentService;
    }
    // POST: api/Message
    [HttpPost("SendMessage")]
    public async Task<ActionResult<Message>> PostMessage(Message message)
    {
        _context.Messages.Add(message);
        await _context.SaveChangesAsync();
        return CreatedAtAction("GetMessage", new { id = message.Id }, message);
    }

    // Additional methods to retrieve messages...
    [HttpGet("GetMessages")]
    public async Task<ActionResult<IEnumerable<Message>>> GetMessages()
    {
        return await _context.Messages.ToListAsync();
    }

    [HttpGet("GetGroupedMessagesByUser/{userId}")]
    public async Task<ActionResult<IEnumerable<GroupedMessageDTO>>> GetGroupedMessagesByUser(string userId)
    {
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
        // Step 1: Retrieve the conversation and related messages
        if (!Guid.TryParse(conversationId, out Guid conversationGuid))
        {
            return BadRequest("Invalid conversationId format.");
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
        if (!Guid.TryParse(request.ConversationId, out Guid conversationGuid))
        {
            return BadRequest("Invalid ConversationId format.");
        }

        var messages = await _context.Messages
            .Where(m => m.ConversationId == conversationGuid && m.ReceiverId == request.UserId && !m.IsRead)
            .ToListAsync();

        foreach (var message in messages)
        {
            message.IsRead = true;
        }

        await _context.SaveChangesAsync();

        return Ok(new { Message = "Messages marked as read" });
    }

    [HttpPost("UploadAttachment")]
    [RequestSizeLimit(10_000_000)] // 10 MB
    [Authorize]
    public async Task<IActionResult> UploadAttachment(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("Invalid file.");
        }

        try
        {
            var container = _blobServiceClient.GetBlobContainerClient("message-attachments");
            await container.CreateIfNotExistsAsync();

            var ext = Path.GetExtension(file.FileName);
            var name = $"{Guid.NewGuid()}{ext}";
            var blob = container.GetBlobClient(name);

            await using (var stream = file.OpenReadStream())
            {
                await blob.UploadAsync(stream, new BlobHttpHeaders { ContentType = file.ContentType });
            }

            var storedContent = _attachmentService.NormalizeForStorage(blob.Uri.ToString());
            var sasUri = _attachmentService.GetClientReadableContent(storedContent);
            return Ok(new { url = sasUri });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

}
