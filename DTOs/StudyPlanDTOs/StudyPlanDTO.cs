// using Newtonsoft.Json;


public class StudyPlanDTO
{
    // [JsonProperty("study_plan")]
    public StudyPlanDetail? StudyPlan { get; set; }
}

public class StudyPlanDetail
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public Introduction? Introduction { get; set; }
    public float ProgressPercentage { get; set; } // Overall progress (excluding advanced topics)
    public float AdvancedTopicProgressPercentage { get; set; } // Progress for advanced topics
    public List<Lesson>? Prerequisite { get; set; }
    public List<Lesson>? MainCurriculum { get; set; }
    public List<Lesson>? AdvancedTopics { get; set; }
    public bool Completed { get; set; }
}

public class Introduction
{
    public string? Description { get; set; }
    public List<string>? Keywords { get; set; }
}

public class Lesson
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<string>? Keywords { get; set; }
    public List<ResourceDTO>? Resources { get; set; }
    public int FinishedResourcesCount { get; set; }
    public float ProgressPercentage { get; set; }
}