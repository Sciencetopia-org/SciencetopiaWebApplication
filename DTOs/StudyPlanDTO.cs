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

    // [JsonProperty("main_curriculum")]
    public List<Lesson>? MainCurriculum { get; set; }
}

public class Lesson
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<Resource>? Resources { get; set; } = new List<Resource>();
}

public class Resource
{
    public string? Link { get; set; }
    // Add other properties for Resource if needed
}

