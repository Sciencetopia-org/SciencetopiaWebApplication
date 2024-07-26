public class StudyGroup
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }

    public List<GroupMember>? MemberIds { get; set; }
    public string Role { get; set; } = string.Empty; // Add 'required' modifier and initialize with an empty string
    // Other properties like members, posts, etc.

    public StudyGroup()
    {
        MemberIds = new List<GroupMember>();
    }
}

public class GroupMember
{
    public string? Id { get; set; }
    public string? Role {get; set;}
}