using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Controllers.Messaging;

namespace SciencetopiaWebApplication.Tests;

public class NotificationControllerSecurityTests
{
    [Fact]
    public void NotificationController_Requires_Authentication()
    {
        var attribute = typeof(NotificationController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
    }

    [Fact]
    public void Global_Notification_Endpoints_Are_Admin_Only()
    {
        var sendAttribute = typeof(NotificationController)
            .GetMethod(nameof(NotificationController.PostNotification))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .SingleOrDefault();
        var listAttribute = typeof(NotificationController)
            .GetMethod(nameof(NotificationController.GetNotifications))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.Equal("administrator", sendAttribute?.Roles);
        Assert.Equal("administrator", listAttribute?.Roles);
    }

    [Fact]
    public async Task User_Can_Read_Only_Own_Notifications()
    {
        using var scope = new TestScope();
        await scope.AddUserAsync("user-a", "alice");
        await scope.AddUserAsync("user-b", "bob");
        scope.Db.Notifications.AddRange(
            new Notification { UserId = "user-a", Content = "mine" },
            new Notification { UserId = "user-b", Content = "theirs" });
        await scope.Db.SaveChangesAsync();

        var controller = CreateController(scope, "user-a");
        var own = await controller.GetNotificationsForUser("user-a");
        var other = await controller.GetNotificationsForUser("user-b");

        var ok = Assert.IsType<OkObjectResult>(own);
        var notifications = Assert.IsAssignableFrom<IEnumerable<NotificationDTO>>(ok.Value);
        var notification = Assert.Single(notifications);
        Assert.Equal("mine", notification.Content);
        Assert.IsType<ForbidResult>(other);
    }

    [Fact]
    public async Task User_Cannot_Mark_Another_Users_Notifications_Read()
    {
        using var scope = new TestScope();
        await scope.AddUserAsync("user-a", "alice");
        await scope.AddUserAsync("user-b", "bob");
        scope.Db.Notifications.Add(new Notification
        {
            UserId = "user-b",
            Content = "theirs",
            IsRead = false
        });
        await scope.Db.SaveChangesAsync();

        var controller = CreateController(scope, "user-a");
        var result = await controller.MarkNotificationsAsRead("user-b");

        Assert.IsType<ForbidResult>(result);
        Assert.False(scope.Db.Notifications.Single().IsRead);
    }

    [Fact]
    public async Task Admin_Can_Read_Global_Notifications()
    {
        using var scope = new TestScope();
        await scope.AddUserAsync("admin", "admin", admin: true);
        scope.Db.Notifications.Add(new Notification { UserId = "admin", Content = "system" });
        await scope.Db.SaveChangesAsync();

        var controller = CreateController(scope, "admin", admin: true);
        var result = await controller.GetNotifications();

        Assert.Single(result.Value!);
    }

    private static NotificationController CreateController(TestScope scope, string userId, bool admin = false)
    {
        var controller = new NotificationController(scope.Db, scope.NotificationService);
        controller.SetUser(userId, admin);
        return controller;
    }
}
