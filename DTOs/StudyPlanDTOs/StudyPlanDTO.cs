using Sciencetopia.Models;

public class StudyPlanDTO
{
    public StudyPlanDetail? StudyPlan { get; set; }
}

public class StudyPlanDetail
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public Introduction? Introduction { get; set; }
    public float ProgressPercentage { get; set; }
    public float AdvancedTopicProgressPercentage { get; set; }
    public List<Lesson>? Prerequisite { get; set; }
    public List<Lesson>? MainCurriculum { get; set; }
    public List<Lesson>? AdvancedTopics { get; set; }
    public bool Completed { get; set; }
    // Plan-level tags (optional)
    public List<TagDTO>? Tags { get; set; }
}

public class Introduction
{
    public string? Description { get; set; }
    public List<Node>? AssociatedKnowledgeNodes { get; set; }
}

public class Lesson
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<Node>? AssociatedKnowledgeNodes { get; set; }
    public List<ResourceDTO>? Resources { get; set; }
    public int FinishedResourcesCount { get; set; }
    public float ProgressPercentage { get; set; }
    // Lesson-level tags (optional)
    public List<TagDTO>? Tags { get; set; }
}
