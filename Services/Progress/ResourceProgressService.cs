using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Sciencetopia.Hubs;
using Sciencetopia.DTOs;
using Sciencetopia.Repositories.Neo4j;
using Sciencetopia.Services.Plans;

namespace Sciencetopia.Services.Progress;

    public class ResourceProgressService : IResourceProgressService
    {
        private readonly INeo4jProgressRepository _repo;
        private readonly IHubContext<StudyHub> _hub;
        private readonly IMemoryCache _cache;
        private readonly IPersonalPlanEnrollmentService _personalGroups;
        private static readonly TimeSpan CompletedResourceIdsCacheTtl = TimeSpan.FromSeconds(30);

    public ResourceProgressService(
        INeo4jProgressRepository repo,
        IHubContext<StudyHub> hub,
        IMemoryCache cache,
        IPersonalPlanEnrollmentService personalGroups)
    {
        _repo = repo;
        _hub = hub;
        _cache = cache;
        _personalGroups = personalGroups;
    }

    private async Task<string> GetPersonalGroupSubjectIdAsync(string userId)
        => (await _personalGroups.EnsurePersonalGroupProjectionAsync(userId)).ToString();

    private static string GetLessonCompletedCacheKey(string userId, Guid lessonId)
        => $"progress:lesson-completed:{userId}:{lessonId}";

    private static string GetPlanCompletedCacheKey(string userId, Guid planId)
        => $"progress:plan-completed:{userId}:{planId}";

    private void InvalidateCompletionCaches(string userId, Guid? planId = null, Guid? lessonId = null)
    {
        if (planId.HasValue && planId.Value != Guid.Empty)
        {
            _cache.Remove(GetPlanCompletedCacheKey(userId, planId.Value));
        }

        if (lessonId.HasValue && lessonId.Value != Guid.Empty)
        {
            _cache.Remove(GetLessonCompletedCacheKey(userId, lessonId.Value));
        }
    }

    public async Task<ResourceProgressResult> CompleteAsync(string userId, Guid resourceId, CompleteResourceDto dto)
    {
        var subjectId = await GetPersonalGroupSubjectIdAsync(userId);
        await _repo.CompleteResourceAsync(subjectId, resourceId, DateTime.UtcNow, dto.source, dto.device, dto.spentSeconds);
        InvalidateCompletionCaches(userId, dto.planId, dto.lessonId);

        double planProgress = 0;
        double lessonProgress = 0;
        int lessonCompleted = 0;
        int lessonTotal = 0;

        if (dto.planId.HasValue)
        {
            var plan = await _repo.GetMyPlanProgressAsync(subjectId, dto.planId.Value);
            planProgress = plan.planProgress;
        }

        if (dto.lessonId.HasValue)
        {
            var les = await _repo.GetMyLessonProgressAsync(subjectId, dto.lessonId.Value);
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

    public async Task UndoAsync(string userId, Guid resourceId, Guid? planId = null, Guid? lessonId = null)
    {
        var subjectId = await GetPersonalGroupSubjectIdAsync(userId);
        await _repo.UndoCompleteResourceAsync(subjectId, resourceId);
        InvalidateCompletionCaches(userId, planId, lessonId);
        // No broadcast here, clients typically refresh on demand.
    }

    public async Task<UserPlanProgressDto> GetPlanProgressAsync(string userId, Guid planId)
        => await _repo.GetMyPlanProgressWithLessonsAsync(await GetPersonalGroupSubjectIdAsync(userId), planId);

    public async Task<(UserPlanProgressDto progress, HashSet<Guid> completedResourceIds)> GetPlanProgressSnapshotAsync(string userId, Guid planId)
        => await _repo.GetMyPlanProgressSnapshotAsync(await GetPersonalGroupSubjectIdAsync(userId), planId);

    public async Task<Dictionary<Guid, UserPlanProgressDto>> GetPlanProgressByPlanIdsAsync(string userId, IEnumerable<Guid> planIds)
        => await _repo.GetMyPlanProgressByPlanIdsAsync(await GetPersonalGroupSubjectIdAsync(userId), planIds);

    public async Task<UserLessonProgressDto> GetLessonProgressAsync(string userId, Guid lessonId)
        => await _repo.GetMyLessonProgressAsync(await GetPersonalGroupSubjectIdAsync(userId), lessonId);

    public Task<CohortSummaryDto> GetCohortSummaryAsync(Guid cohortId)
        => _repo.GetCohortSummaryAsync(cohortId);

    public Task<List<LessonStatDto>> GetCohortLessonStatsAsync(Guid cohortId)
        => _repo.GetCohortLessonStatsAsync(cohortId);

        public Task<List<LeaderboardItemDto>> GetCohortLeaderboardAsync(Guid cohortId, int top)
            => _repo.GetCohortLeaderboardAsync(cohortId, top);

        public async Task ToggleByLinkAsync(string userId, string resourceLink, string? source, string? device)
        {
            var subjectId = await GetPersonalGroupSubjectIdAsync(userId);
            // No cohort broadcast here due to missing plan context.
            await _repo.ToggleCompleteByLinkAsync(subjectId, resourceLink, DateTime.UtcNow, source, device);
        }

        public Task<HashSet<Guid>> GetCompletedResourceIdsForPlanAsync(string userId, Guid planStableId)
        {
            if (planStableId == Guid.Empty)
            {
                return Task.FromResult(new HashSet<Guid>());
            }

            var cacheKey = GetPlanCompletedCacheKey(userId, planStableId);
            if (_cache.TryGetValue<HashSet<Guid>>(cacheKey, out var cached) && cached != null)
            {
                return Task.FromResult(new HashSet<Guid>(cached));
            }

            return GetAndCacheCompletedIdsAsync(cacheKey, async () => await _repo.GetCompletedResourceIdsForPlanAsync(await GetPersonalGroupSubjectIdAsync(userId), planStableId));
        }

        public Task<HashSet<Guid>> GetCompletedResourceIdsForLessonAsync(string userId, Guid lessonId)
        {
            if (lessonId == Guid.Empty)
            {
                return Task.FromResult(new HashSet<Guid>());
            }

            var cacheKey = GetLessonCompletedCacheKey(userId, lessonId);
            if (_cache.TryGetValue<HashSet<Guid>>(cacheKey, out var cached) && cached != null)
            {
                return Task.FromResult(new HashSet<Guid>(cached));
            }

            return GetAndCacheCompletedIdsAsync(cacheKey, async () => await _repo.GetCompletedResourceIdsForLessonAsync(await GetPersonalGroupSubjectIdAsync(userId), lessonId));
        }

        public async Task<List<ResourceCompletedStatusDto>> GetCompletedStatusAsync(string userId, IEnumerable<Guid> resourceIds)
        {
            var set = await _repo.GetCompletedResourceIdsAsync(await GetPersonalGroupSubjectIdAsync(userId), resourceIds);
            return resourceIds.Select(id => new ResourceCompletedStatusDto(id, set.Contains(id))).ToList();
        }

        private async Task<HashSet<Guid>> GetAndCacheCompletedIdsAsync(string cacheKey, Func<Task<HashSet<Guid>>> factory)
        {
            var ids = await factory();
            _cache.Set(cacheKey, new HashSet<Guid>(ids), CompletedResourceIdsCacheTtl);
            return ids;
        }
    }
