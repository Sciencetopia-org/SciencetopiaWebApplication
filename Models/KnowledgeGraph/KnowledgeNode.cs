public class KnowledgeNode
{
    public Guid? Id { get; set; }
    public Guid StableId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "Draft";
    public bool IsCurrent { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }

    public string? Name { get; set; }
    public string? Description { get; set; }
    public Guid? DefaultL10nSetId { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
