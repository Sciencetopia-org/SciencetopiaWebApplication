public class EditNodeRequest
{
    public Guid NodeId { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }

    // 建议关联的新标签 ID 集合（覆盖原有标签）
    public List<Guid> TagIds { get; set; } = new();
    // 关联的新标签名称集合（覆盖原有标签）
    public List<string> TagNames { get; set; } = new();
}
