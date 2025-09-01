using Sciencetopia.DTOs;

namespace Sciencetopia.Repositories.Neo4j;

public interface INeo4jProgressRepository
{
    Task CompleteResourceAsync(string userId, Guid resourceId, DateTime completedAt, string? source, string? device, int? spentSeconds);
    Task UndoCompleteResourceAsync(string userId, Guid resourceId);

    Task<UserPlanProgressDto>   GetMyPlanProgressAsync(string userId, Guid planId);
    Task<UserLessonProgressDto> GetMyLessonProgressAsync(string userId, Guid lessonId);
    Task<UserPlanProgressDto>   GetMyPlanProgressWithLessonsAsync(string userId, Guid planId);

    Task<CohortSummaryDto> GetCohortSummaryAsync(Guid cohortId);
    Task<List<LessonStatDto>> GetCohortLessonStatsAsync(Guid cohortId);
    Task<List<LeaderboardItemDto>> GetCohortLeaderboardAsync(Guid cohortId, int top);

    Task<List<Guid>> GetCohortsForUserAndPlanAsync(string userId, Guid planId);

    Task<bool> ToggleCompleteByLinkAsync(string userId, string resourceLink, DateTime now, string? source, string? device);

    Task<HashSet<Guid>> GetCompletedResourceIdsAsync(string userId, IEnumerable<Guid> resourceIds);
}
