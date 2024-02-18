// using Newtonsoft.Json;

public class StudyPlanDTO
{
    // [JsonProperty("study_plan")]
    public StudyPlanDetail? StudyPlan { get; set; }
}

public class StudyPlanDetail
{
    public string? Title { get; set; }
    public List<Lesson>? Prerequisite { get; set; }
    public List<Lesson>? MainCurriculum { get; set; }
}
public class Lesson
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<Resource>? Resources { get; set; }
    public int FinishedResourcesCount { get; set; }
    public float ProgressPercentage { get; set; }
}

public class Resource
{
    public string? Link { get; set; }
    public bool Learned { get; set; }
}
