namespace Sciencetopia.Constants;

public static class StudyPlanStepTypes
{
    public const string Prerequisite = "PREREQUISITE";
    public const string MainCurriculum = "MAIN_CURRICULUM";
    public const string AdvancedTopic = "ADVANCED_TOPIC";

    // Legacy values kept temporarily for backwards compatibility with existing graph data.
    public static readonly string[] AdvancedTopicAliases =
    {
        AdvancedTopic,
        "advancedTopics"
    };
}
