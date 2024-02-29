using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;

[ApiController]
[Route("api/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public NotificationController(ApplicationDbContext context)
    {
        _context = context;
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
}