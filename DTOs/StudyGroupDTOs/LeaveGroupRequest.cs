public class LeaveGroupRequest
{
    public required string UserId { get; set; }
    public required string GroupId { get; set; }
}

public class DissolveGroupRequest
{
    public string? UserId { get; set; }
    public string? GroupId { get; set; }
}
