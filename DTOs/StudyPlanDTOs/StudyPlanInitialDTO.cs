using Sciencetopia.Models;

public class StudyPlanInitialDTO
{
    public StudyPlanInitial? StudyPlan { get; set; }
}

public class StudyPlanInitial
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public Introduction? Introduction { get; set; }
    public List<LessonInitial>? Prerequisite { get; set; }
    public List<LessonInitial>? MainCurriculum { get; set; }
    public List<LessonInitial>? AdvancedTopics { get; set; }
    public bool Completed { get; set; }
}

public class LessonInitial
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<Node>? AssociatedKnowledgeNodes { get; set; }
    public List<ResourceDTO>? Resources { get; set; }
}
