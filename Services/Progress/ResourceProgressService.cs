using Microsoft.AspNetCore.SignalR;
using Sciencetopia.Hubs;
using Sciencetopia.DTOs;
using Sciencetopia.Repositories.Neo4j;

namespace Sciencetopia.Services.Progress;

    public class ResourceProgressService : IResourceProgressService
    {
        private readonly INeo4jProgressRepository _repo;
        private readonly IHubContext<StudyHub> _hub;

    public ResourceProgressService(INeo4jProgressRepository repo, IHubContext<StudyHub> hub)
    {
        _repo = repo;
        _hub = hub;
    }

    public async Task<ResourceProgressResult> CompleteAsync(string userId, Guid resourceId, CompleteResourceDto dto)
    {
        await _repo.CompleteResourceAsync(userId, resourceId, DateTime.UtcNow, dto.source, dto.device, dto.spentSeconds);

        double planProgress = 0;
        double lessonProgress = 0;
        int lessonCompleted = 0;
        int lessonTotal = 0;

        if (dto.planId.HasValue)
        {
            var plan = await _repo.GetMyPlanProgressAsync(userId, dto.planId.Value);
            planProgress = plan.planProgress;
        }

        if (dto.lessonId.HasValue)
        {
            var les = await _repo.GetMyLessonProgressAsync(userId, dto.lessonId.Value);
            lessonProgress = les.lessonProgress;
            lessonCompleted = les.completedCount;
            lessonTotal = les.totalResources;
        }

        // Broadcast to cohorts if applicable
        if (dto.planId.HasValue)
        {
            var cohortIds = await _repo.GetCohortsForUserAndPlanAsync(userId, dto.planId.Value);
            foreach (var cid in cohortIds)
            {
                // Push resourceCompleted (lightweight)
                await _hub.Clients.Group($"cohort:{cid}").SendAsync("resourceCompleted", new
                {
                    cohortId = cid,
                    userId,
                    resourceId,
                    planProgress
                });

                // Recompute and push cohort summary
                var summary = await _repo.GetCohortSummaryAsync(cid);
                await _hub.Clients.Group($"cohort:{cid}").SendAsync("progressUpdated", new { cohortId = cid, summary });
            }
        }

        return new ResourceProgressResult(planProgress, lessonProgress, lessonCompleted, lessonTotal);
    }

    public async Task UndoAsync(string userId, Guid resourceId)
    {
        await _repo.UndoCompleteResourceAsync(userId, resourceId);
        // No broadcast here, clients typically refresh on demand.
    }

    public Task<UserPlanProgressDto> GetPlanProgressAsync(string userId, Guid planId)
        => _repo.GetMyPlanProgressWithLessonsAsync(userId, planId);

    public Task<UserLessonProgressDto> GetLessonProgressAsync(string userId, Guid lessonId)
        => _repo.GetMyLessonProgressAsync(userId, lessonId);

    public Task<CohortSummaryDto> GetCohortSummaryAsync(Guid cohortId)
        => _repo.GetCohortSummaryAsync(cohortId);

    public Task<List<LessonStatDto>> GetCohortLessonStatsAsync(Guid cohortId)
        => _repo.GetCohortLessonStatsAsync(cohortId);

        public Task<List<LeaderboardItemDto>> GetCohortLeaderboardAsync(Guid cohortId, int top)
            => _repo.GetCohortLeaderboardAsync(cohortId, top);

        public async Task ToggleByLinkAsync(string userId, string resourceLink, string? source, string? device)
        {
            // Use repository toggle by link for backward-compatible endpoint.
            // No cohort broadcast here due to missing plan context.
            await _repo.ToggleCompleteByLinkAsync(userId, resourceLink, DateTime.UtcNow, source, device);
        }

        public async Task<List<ResourceCompletedStatusDto>> GetCompletedStatusAsync(string userId, IEnumerable<Guid> resourceIds)
        {
            var set = await _repo.GetCompletedResourceIdsAsync(userId, resourceIds);
            return resourceIds.Select(id => new ResourceCompletedStatusDto(id, set.Contains(id))).ToList();
        }
    }
