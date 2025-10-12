namespace Sciencetopia.Models;

public class LessonTagAssignment
{
    public Guid LessonStableId { get; set; }
    public int LessonVersionNumber { get; set; }
    public Guid TagStableId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
