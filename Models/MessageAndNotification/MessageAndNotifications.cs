using Microsoft.AspNetCore.Identity;
using Sciencetopia.Models;
using System;

public class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<Message> Messages { get; set; } = new List<Message>();
}

public class Message
{
    public Guid? Id { get; set; } = Guid.NewGuid();
    public string? Content { get; set; }
    public DateTimeOffset SentTime { get; set; }
    public string? SenderId { get; set; }
    public ApplicationUser? Sender { get; set; } // Reference ApplicationUser here
    public string? ReceiverId { get; set; }
    public ApplicationUser? Receiver { get; set; } // And here
    public Guid? ConversationId { get; set; }
    public Conversation? Conversation { get; set; }
    public bool IsRead { get; set; }
}

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Content { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsRead { get; set; }
    public string? UserId { get; set; }
    public string? Type { get; set; }  // Notification type
    public string? Data { get; set; }  // JSON string containing additional data
    public ApplicationUser? User { get; set; } // Reference ApplicationUser here
}