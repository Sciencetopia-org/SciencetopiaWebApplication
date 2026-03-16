public class KnowledgeNodeUserStateDTO
{
    public string NodeId { get; set; } = string.Empty;
    public bool IsFavorited { get; set; }
    public bool IsLearned { get; set; }
    public bool IsPartiallyLearned { get; set; }
    public int TotalResourceCount { get; set; }
    public int CompletedResourceCount { get; set; }
}
