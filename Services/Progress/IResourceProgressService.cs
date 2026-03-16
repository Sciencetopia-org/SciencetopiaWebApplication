using Sciencetopia.DTOs;

namespace Sciencetopia.Services.Progress;

public interface IResourceProgressService
{
    Task<ResourceProgressResult> CompleteAsync(string userId, Guid resourceId, CompleteResourceDto dto);
    Task UndoAsync(string userId, Guid resourceId, Guid? planId = null, Guid? lessonId = null);
    Task ToggleByLinkAsync(string userId, string resourceLink, string? source, string? device);

    Task<UserPlanProgressDto>   GetPlanProgressAsync(string userId, Guid planId);
    Task<(UserPlanProgressDto progress, HashSet<Guid> completedResourceIds)> GetPlanProgressSnapshotAsync(string userId, Guid planId);
    Task<Dictionary<Guid, UserPlanProgressDto>> GetPlanProgressByPlanIdsAsync(string userId, IEnumerable<Guid> planIds);
    Task<UserLessonProgressDto> GetLessonProgressAsync(string userId, Guid lessonId);

    Task<CohortSummaryDto> GetCohortSummaryAsync(Guid cohortId);
    Task<List<LessonStatDto>> GetCohortLessonStatsAsync(Guid cohortId);
    Task<List<LeaderboardItemDto>> GetCohortLeaderboardAsync(Guid cohortId, int top);

    Task<HashSet<Guid>> GetCompletedResourceIdsForPlanAsync(string userId, Guid planStableId);
    Task<HashSet<Guid>> GetCompletedResourceIdsForLessonAsync(string userId, Guid lessonId);
    Task<List<ResourceCompletedStatusDto>> GetCompletedStatusAsync(string userId, IEnumerable<Guid> resourceIds);
}

public record ResourceProgressResult(double planProgress, double lessonProgress, int lessonCompletedCount, int lessonTotalResources);
