public class MessageDTO
{
    public string? Id { get; set; }
    public string? Content { get; set; }
    public DateTime SentTime { get; set; }
    public string? SenderId { get; set; }
    public string? ReceiverId { get; set; }
    // Exclude navigation properties like Sender, Receiver, and Conversation
}

public class GroupedMessageDTO
{
    public string? ConversationId { get; set; }
    public List<MessageWithUserDetailsDTO>? Messages { get; set; }
}

public class MessageWithUserDetailsDTO
{
    public string? Id { get; set; }
    public string? Content { get; set; }
    public DateTime SentTime { get; set; }
    public UserDetailsDTO? Sender { get; set; }
}

public class UserDetailsDTO
{
    public string? Id { get; set; }
    public string? UserName { get; set; }
    public string? AvatarUrl { get; set; }
}