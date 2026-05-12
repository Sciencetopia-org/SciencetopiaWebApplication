namespace Sciencetopia.DTOs;

public record CompleteResourceDto(Guid? planId, Guid? lessonId, Guid? knowledgeNodeId, string? source, string? device, int? spentSeconds);
public record UserLessonProgressDto(Guid lessonId, double lessonProgress, int completedCount, int totalResources);
public record UserPlanProgressDto(double planProgress, IEnumerable<UserLessonProgressDto> perLesson, double advancedTopicProgress);
public record CohortSummaryDto(double avgProgress, int memberCount);
public record LessonStatDto(Guid lessonId, string lessonTitle, double lessonAvgProgress, int completedCount, int totalResources);
public record LeaderboardItemDto(string userId, string displayName, double progress, string avatarUrl = "");
public record RankedLeaderboardItemDto(string userId, string displayName, double progress, int rank, bool isMe, string avatarUrl = "");
public record CohortDashboardCohortDto(
    Guid id,
    string? title,
    Guid? studyGroupId,
    Guid studyPlanId,
    Guid studyPlanStableId,
    int? pinnedVersionNumber,
    string? enrollMode,
    string? visibility);
public record CohortDashboardMeDto(bool isEnrolled, double progress, int? rank, bool shareMetrics);
public record CohortDashboardDto(
    CohortDashboardCohortDto cohort,
    CohortSummaryDto summary,
    CohortDashboardMeDto me,
    IEnumerable<RankedLeaderboardItemDto> leaderboard,
    IEnumerable<LessonStatDto> lessons);
