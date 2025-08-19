// Models/ProjectedRelationDTO.cs
public class ProjectedRelationDTO
{
    public Guid SourceId { get; set; }
    public Guid TargetId { get; set; }
    public int Weight { get; set; }
}