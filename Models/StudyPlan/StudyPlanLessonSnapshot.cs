namespace Sciencetopia.Models;

public class StudyPlanLessonSnapshot
{
    public Guid StudyPlanStableId { get; set; }
    public int StudyPlanVersionNumber { get; set; }
    public Guid LessonStableId { get; set; }
    public int LessonVersionNumber { get; set; }
    public string StepType { get; set; } = string.Empty;
    public int StepOrder { get; set; }
}
