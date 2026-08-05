using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Controllers.Messaging;

namespace SciencetopiaWebApplication.Tests;

public class MessageControllerSecurityTests
{
    [Fact]
    public void MessageController_Requires_Authentication()
    {
        var attribute = typeof(MessageController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
    }

    [Fact]
    public void GetMessages_Is_Admin_Only()
    {
        var attribute = typeof(MessageController)
            .GetMethod(nameof(MessageController.GetMessages))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal("administrator", attribute.Roles);
    }

    [Fact]
    public async Task User_Cannot_Read_Another_Users_Message()
    {
        using var scope = new TestScope();
        await scope.AddUserAsync("user-a", "alice");
        await scope.AddUserAsync("user-b", "bob");
        await scope.AddUserAsync("user-c", "carol");
        var conversationId = Guid.NewGuid();
        scope.Db.Conversations.Add(new Conversation { Id = conversationId });
        scope.Db.Messages.Add(new Message
        {
            ConversationId = conversationId,
            SenderId = "user-b",
            ReceiverId = "user-c",
            Content = "private"
        });
        await scope.Db.SaveChangesAsync();

        var controller = CreateController(scope, "user-a");
        var result = await controller.GetConversation(conversationId.ToString());

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Fact]
    public async Task User_Cannot_Mark_Another_Users_Message_Read()
    {
        using var scope = new TestScope();
        await scope.AddUserAsync("user-a", "alice");
        await scope.AddUserAsync("user-b", "bob");
        await scope.AddUserAsync("user-c", "carol");
        var conversationId = Guid.NewGuid();
        scope.Db.Conversations.Add(new Conversation { Id = conversationId });
        scope.Db.Messages.Add(new Message
        {
            ConversationId = conversationId,
            SenderId = "user-b",
            ReceiverId = "user-c",
            Content = "private",
            IsRead = false
        });
        await scope.Db.SaveChangesAsync();

        var controller = CreateController(scope, "user-a");
        var result = await controller.MarkAsRead(new MarkAsReadRequest
        {
            ConversationId = conversationId.ToString(),
            UserId = "user-c"
        });

        Assert.IsType<ForbidResult>(result);
        Assert.False(scope.Db.Messages.Single().IsRead);
    }

    [Fact]
    public async Task ClientSuppliedSenderId_IsIgnored()
    {
        using var scope = new TestScope();
        await scope.AddUserAsync("user-a", "alice");
        await scope.AddUserAsync("user-b", "bob");
        var conversationId = Guid.NewGuid();
        scope.Db.Conversations.Add(new Conversation { Id = conversationId });
        scope.Db.Messages.Add(new Message
        {
            ConversationId = conversationId,
            SenderId = "user-b",
            ReceiverId = "user-a",
            Content = "existing"
        });
        await scope.Db.SaveChangesAsync();

        var controller = CreateController(scope, "user-a");
        var result = await controller.PostMessage(new SendMessageRequest(
            conversationId.ToString(),
            "user-b",
            "reply"));

        Assert.IsType<OkObjectResult>(result.Result);
        var created = scope.Db.Messages
            .OrderByDescending(m => m.SentTime)
            .First();
        Assert.Equal("user-a", created.SenderId);
        Assert.Equal("user-b", created.ReceiverId);
    }

    [Fact]
    public async Task Admin_Can_Access_Explicit_Admin_Message_List()
    {
        using var scope = new TestScope();
        await scope.AddUserAsync("admin", "admin", admin: true);
        scope.Db.Messages.Add(new Message { SenderId = "admin", ReceiverId = "admin", Content = "audit" });
        await scope.Db.SaveChangesAsync();

        var controller = CreateController(scope, "admin", admin: true);
        var result = await controller.GetMessages();

        Assert.Single(result.Value!);
    }

    private static MessageController CreateController(TestScope scope, string userId, bool admin = false)
    {
        var controller = new MessageController(
            scope.Db,
            scope.UserService,
            new Azure.Storage.Blobs.BlobServiceClient("UseDevelopmentStorage=true"),
            scope.AttachmentService,
            new AllowAllContentModerationService());
        controller.SetUser(userId, admin);
        return controller;
    }
}
