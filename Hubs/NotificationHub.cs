using Microsoft.AspNetCore.SignalR;
using Sciencetopia.Data;

namespace Sciencetopia.Hubs
{
    public class NotificationHub : Hub
    {
        private readonly ApplicationDbContext _context;

        public NotificationHub(ApplicationDbContext context)
        {
            _context = context;
        }

        // Sends a notification to a specific user
        public async Task SendNotificationToUser(string userId, string content)
        {
            var notification = new Notification
            {
                Content = content,
                CreatedAt = DateTime.UtcNow,
                IsRead = false,
                UserId = userId
            };

            // Here you would typically save the notification to your database
            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            // Assuming you have a way to map userId to connectionId or user groups
            await Clients.User(userId).SendAsync("ReceiveNotification", notification);
        }

        // Method to mark a notification as read, could be called from the client
        public async Task MarkNotificationAsRead(string notificationId)
        {
            // Fetch the notification from the database using notificationId
            var notification = await _context.Notifications.FindAsync(notificationId);
            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }

            // Notify the client (optional, based on your app's needs)
            await Clients.Caller.SendAsync("NotificationRead", notificationId);
        }

        // Additional methods as needed...
    }
}
