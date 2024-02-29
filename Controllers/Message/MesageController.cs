using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Services;

[ApiController]
[Route("api/[controller]")]
public class MessageController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly UserService _userService;

    public MessageController(ApplicationDbContext context, UserService userService)
    {
        _context = context;
        _userService = userService;
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

    [HttpGet("GetMessageByReceiver/{receiverId}")]
    public async Task<ActionResult<IEnumerable<Message>>> GetMessageByReceiver(string receiverId)
    {
        var groupedMessages = await _context.Messages
            .Include(m => m.Sender)
            .Where(m => m.ReceiverId == receiverId)
            .GroupBy(m => m.ConversationId)
            .Select(group => new GroupedMessageDTO
            {
                ConversationId = group.Key,
                Messages = group.OrderBy(m => m.SentTime) // Order messages by SentTime
                    .Select(m => new MessageWithUserDetailsDTO
                    {
                        Id = m.Id,
                        Content = m.Content,
                        SentTime = m.SentTime,
                        Sender = new UserDetailsDTO
                        {
                            Id = m.Sender.Id,
                            UserName = m.Sender.UserName,
                        }
                    }).ToList()
            })
            .ToListAsync();

        // Step 2: Fetch avatar URLs for each sender in a separate step
        foreach (var group in groupedMessages)
        {
            foreach (var message in group.Messages)
            {
                if (message.Sender != null)
                {
                    var avatarUrl = await _userService.FetchUserAvatarUrlByIdAsync(message.Sender.Id!);
                    message.Sender.AvatarUrl = avatarUrl; // Assuming you add a placeholder for AvatarUrl in the message object
                }
            }
        }

        return Ok(groupedMessages);
    }
}