namespace Sciencetopia.Constants;

public static class StudyPlanStepTypes
{
    public const string Prerequisite = "PREREQUISITE";
    public const string MainCurriculum = "MAIN_CURRICULUM";
    public const string AdvancedTopic = "ADVANCED_TOPIC";

    public static readonly string[] AdvancedTopicAliases =
    {
        AdvancedTopic,
        "advancedTopics"
    };
}
