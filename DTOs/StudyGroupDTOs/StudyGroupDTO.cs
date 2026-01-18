public class StudyGroupDTO
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    // Optional: up to 10 existing tag IDs from SQL Tags table
    public List<Guid>? TagIds { get; set; }
    // Optional: user-entered new tag names (will be created if not exists)
    public List<string>? NewTagNames { get; set; }
}
