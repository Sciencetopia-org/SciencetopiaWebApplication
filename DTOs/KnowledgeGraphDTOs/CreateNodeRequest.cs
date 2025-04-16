public class CreateNodeRequest
{
    public string? Label { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<string>? Link { get; set; }
    public List<Guid> TagIds { get; set; } = new();         // 选择已有标签
    public List<string> NewTagNames { get; set; } = new();  // 新建标签
}