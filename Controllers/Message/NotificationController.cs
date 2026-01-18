using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;

namespace Sciencetopia.Controllers.Messaging;

[ApiController]
[Route("api/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly NotificationService _notificationService;

    public NotificationController(ApplicationDbContext context, NotificationService notificationService)
    {
        _context = context;
        _notificationService = notificationService;
    }

    [HttpPost("SendNotification")]
    public async Task<ActionResult<Notification>> PostNotification(Notification notification)
    {
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();
        return CreatedAtAction("GetNotification", new { id = notification.Id }, notification);
    }
    
    [HttpGet("GetNotifications")]
    public async Task<ActionResult<IEnumerable<Notification>>> GetNotifications()
    {
        // Logic to retrieve system notifications
        return await _context.Notifications.ToListAsync();
    }

    [HttpGet("GetNotificationsForUser/{userId}")]
    public async Task<IActionResult> GetNotificationsForUser(string userId)
    {
        var notifications = await _notificationService.GetNotificationsForUserAsync(userId);

        if (notifications == null || !notifications.Any())
        {
            return NotFound("No notifications found for this user.");
        }

        return Ok(notifications);
    }

    [HttpPost("MarkAsReadByUser/{userId}")]
    public async Task<IActionResult> MarkNotificationsAsRead(string userId)
    {
        var notifications = await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        // if (notifications == null || !notifications.Any())
        // {
        //     return NotFound("No unread notifications found for this user.");
        // }

        foreach (var notification in notifications)
        {
            notification.IsRead = true;
        }

        await _context.SaveChangesAsync();

        return Ok("Notifications marked as read successfully.");
    }
}
