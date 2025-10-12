namespace Sciencetopia.Models;

public class StudyPlanTagAssignment
{
    public Guid StudyPlanStableId { get; set; }
    public int StudyPlanVersionNumber { get; set; }
    public Guid TagStableId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
