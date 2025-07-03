public class LinkDTO
{
    public Guid? Source { get; set; }
    public Guid? Target { get; set; }
    public string? Relation { get; set; }
    public int? Weight { get; set; } = 1;
}