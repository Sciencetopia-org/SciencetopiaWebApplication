using Microsoft.AspNetCore.Identity;
using Sciencetopia.Models;
using System;

public class Conversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public List<Message> Messages { get; set; } = new List<Message>();
}

public class Message
{
    public string? Id { get; set; } = Guid.NewGuid().ToString();
    public string? Content { get; set; }
    public DateTimeOffset SentTime { get; set; }
    public string? SenderId { get; set; }
    public ApplicationUser? Sender { get; set; } // Reference ApplicationUser here
    public string? ReceiverId { get; set; }
    public ApplicationUser? Receiver { get; set; } // And here
    public string? ConversationId { get; set; }
    public Conversation? Conversation { get; set; }
    public bool IsRead { get; set; }
}

public class Notification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? Content { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsRead { get; set; }
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; } // Reference ApplicationUser here
}