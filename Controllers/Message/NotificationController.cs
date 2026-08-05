using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace Sciencetopia.Controllers.Messaging;

[ApiController]
[Route("api/[controller]")]
[Authorize]
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
    [Authorize(Roles = "administrator")]
    public async Task<ActionResult<Notification>> PostNotification(Notification notification)
    {
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();
        return CreatedAtAction("GetNotification", new { id = notification.Id }, notification);
    }
    
    [HttpGet("GetNotifications")]
    [Authorize(Roles = "administrator")]
    public async Task<ActionResult<IEnumerable<Notification>>> GetNotifications()
    {
        // Logic to retrieve system notifications
        return await _context.Notifications.ToListAsync();
    }

    [HttpGet("GetNotificationsForUser/{userId}")]
    public async Task<IActionResult> GetNotificationsForUser(string userId)
    {
        if (!CanAccessUser(userId)) return Forbid();

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
        if (!CanAccessUser(userId)) return Forbid();

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

    private bool CanAccessUser(string userId)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return !string.IsNullOrEmpty(currentUserId)
            && (string.Equals(userId, currentUserId, StringComparison.OrdinalIgnoreCase) || User.IsInRole("administrator"));
    }
}
