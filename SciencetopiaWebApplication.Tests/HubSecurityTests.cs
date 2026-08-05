using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Sciencetopia.Hubs;
using Sciencetopia.Models;

namespace SciencetopiaWebApplication.Tests;

public class HubSecurityTests
{
    [Fact]
    public void ChatHub_Requires_Authentication()
    {
        var attribute = typeof(ChatHub)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
    }

    [Fact]
    public async Task NonParticipant_Cannot_Join_Conversation_Group()
    {
        using var scope = new TestScope();
        await scope.AddUserAsync("user-a", "alice");
        await scope.AddUserAsync("user-b", "bob");
        await scope.AddUserAsync("user-c", "carol");
        var conversationId = Guid.NewGuid();
        scope.Db.Messages.Add(new Message
        {
            ConversationId = conversationId,
            SenderId = "user-b",
            ReceiverId = "user-c",
            Content = "private"
        });
        await scope.Db.SaveChangesAsync();

        var hub = CreateChatHub(scope, "user-a");

        await Assert.ThrowsAsync<HubException>(() => hub.JoinConversation(conversationId.ToString()));
    }

    [Fact]
    public async Task ClientSuppliedSenderId_IsIgnored_By_ChatHub()
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

        var hub = CreateChatHub(scope, "user-a");
        await hub.SendMessage(conversationId.ToString(), "user-b", "user-b", "reply");

        var created = scope.Db.Messages
            .OrderByDescending(m => m.SentTime)
            .First();
        Assert.Equal("user-a", created.SenderId);
        Assert.Equal("user-b", created.ReceiverId);
    }

    [Fact]
    public void StudyHub_Requires_Authentication()
    {
        var attribute = typeof(StudyHub)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .OfType<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
    }

    [Fact]
    public async Task NonMember_Cannot_Join_Cohort_Group()
    {
        using var scope = new TestScope();
        var cohortId = Guid.NewGuid();
        var hub = CreateStudyHub(scope, "user-a");

        await Assert.ThrowsAsync<HubException>(() => hub.JoinCohort(cohortId));
    }

    [Fact]
    public async Task InactiveMember_Cannot_Join_Cohort_Group()
    {
        using var scope = new TestScope();
        var cohortId = Guid.NewGuid();
        scope.Db.UserGroups.Add(new UserGroupEntity
        {
            GroupId = cohortId,
            UserId = "user-a",
            Status = "Inactive"
        });
        await scope.Db.SaveChangesAsync();
        var hub = CreateStudyHub(scope, "user-a");

        await Assert.ThrowsAsync<HubException>(() => hub.JoinCohort(cohortId));
    }

    [Fact]
    public async Task ActiveMember_Can_Join_Cohort_Group()
    {
        using var scope = new TestScope();
        var cohortId = Guid.NewGuid();
        scope.Db.UserGroups.Add(new UserGroupEntity
        {
            GroupId = cohortId,
            UserId = "user-a",
            Status = "Active"
        });
        await scope.Db.SaveChangesAsync();
        var groups = new Mock<IGroupManager>();
        groups.Setup(g => g.AddToGroupAsync("connection-a", $"cohort:{cohortId}", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var hub = CreateStudyHub(scope, "user-a", groups.Object);

        await hub.JoinCohort(cohortId);

        groups.Verify(g => g.AddToGroupAsync("connection-a", $"cohort:{cohortId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ChatHub CreateChatHub(TestScope scope, string userId)
    {
        var clientProxy = new Mock<IClientProxy>();
        clientProxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var callerProxy = new Mock<ISingleClientProxy>();
        callerProxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubCallerClients>();
        clients.Setup(c => c.User(It.IsAny<string>())).Returns(clientProxy.Object);
        clients.Setup(c => c.Caller).Returns(callerProxy.Object);

        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.UserIdentifier).Returns(userId);
        context.SetupGet(c => c.ConnectionId).Returns($"connection-{userId}");
        context.SetupGet(c => c.User).Returns(SecurityTestHelpers.Principal(userId));

        return new ChatHub(scope.Db, scope.UserService, scope.AttachmentService)
        {
            Context = context.Object,
            Clients = clients.Object,
            Groups = Mock.Of<IGroupManager>()
        };
    }

    private static StudyHub CreateStudyHub(TestScope scope, string userId, IGroupManager? groups = null)
    {
        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.UserIdentifier).Returns(userId);
        context.SetupGet(c => c.ConnectionId).Returns("connection-a");
        context.SetupGet(c => c.User).Returns(SecurityTestHelpers.Principal(userId));

        return new StudyHub(scope.Db)
        {
            Context = context.Object,
            Groups = groups ?? Mock.Of<IGroupManager>()
        };
    }
}
