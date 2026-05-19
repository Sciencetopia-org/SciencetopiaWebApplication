using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Sciencetopia.Data;
using Sciencetopia.DTOs;
using Sciencetopia.Models;
using Sciencetopia.Models.Enums;

namespace Sciencetopia.Services.StudyGroupDiscovery;

public interface IStudyGroupDiscoveryService
{
    Task<StudyGroupRecommendationResponse> GetRecommendationsAsync(
        StudyGroupRecommendationQuery query,
        string? userId,
        CancellationToken ct = default);

    Task<StudyGroupSearchSuggestionsResponse> GetSearchSuggestionsAsync(
        string query,
        int limit,
        string? userId,
        CancellationToken ct = default);

    Task<StudyGroupRecommendationResponse> GetRelatedAsync(
        Guid groupId,
        int limit,
        bool excludeJoined,
        string? userId,
        CancellationToken ct = default);

    Task RecordFeedbackAsync(
        StudyGroupRecommendationFeedbackRequest request,
        string? userId,
        CancellationToken ct = default);
}

public sealed class StudyGroupDiscoveryService : IStudyGroupDiscoveryService
{
    private const string StrategyVersion = "hybrid-rule-v1";
    private readonly ApplicationDbContext _db;
    private readonly IDriver _neo4jDriver;
    private readonly ITagRepository _tagRepository;

    public StudyGroupDiscoveryService(
        ApplicationDbContext db,
        IDriver neo4jDriver,
        ITagRepository tagRepository)
    {
        _db = db;
        _neo4jDriver = neo4jDriver;
        _tagRepository = tagRepository;
    }

    public async Task<StudyGroupRecommendationResponse> GetRecommendationsAsync(
        StudyGroupRecommendationQuery query,
        string? userId,
        CancellationToken ct = default)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 50);
        var parsedTagIds = ParseGuidCsv(query.TagIds);
        var normalizedQuery = NormalizeQuery(query.Q);
        var scene = NormalizeToken(query.Scene, "discover");
        var sortMode = NormalizeSortMode(query.SortMode, string.IsNullOrWhiteSpace(normalizedQuery) ? "personalized" : "relevance");
        var requestId = Guid.NewGuid();

        var profile = await BuildUserProfileAsync(userId, ct);
        var candidateIds = await RetrieveCandidateIdsAsync(
            normalizedQuery,
            parsedTagIds,
            profile,
            sortMode,
            take: Math.Max(page * pageSize * 4, 100),
            ct);

        var ranked = await BuildRankedItemsAsync(
            candidateIds,
            profile,
            normalizedQuery,
            parsedTagIds,
            scene,
            sortMode,
            query.ExcludeJoined,
            query.ExcludeApplied,
            ct);

        var total = ranked.Count;
        var pageItems = ranked
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        await LogRequestAsync(requestId, userId, scene, sortMode, normalizedQuery, candidateIds.Count, pageItems.Count, ct);

        return new StudyGroupRecommendationResponse(
            requestId,
            StrategyVersion,
            page,
            pageSize,
            total,
            !string.IsNullOrWhiteSpace(userId) && profile.HasSignals,
            DateTimeOffset.UtcNow,
            pageItems);
    }

    public async Task<StudyGroupSearchSuggestionsResponse> GetSearchSuggestionsAsync(
        string query,
        int limit,
        string? userId,
        CancellationToken ct = default)
    {
        var q = NormalizeQuery(query);
        if (string.IsNullOrWhiteSpace(q))
        {
            return new StudyGroupSearchSuggestionsResponse(Array.Empty<StudyGroupSearchSuggestionDto>());
        }

        limit = Math.Clamp(limit, 1, 20);
        var suggestions = new List<StudyGroupSearchSuggestionDto>();

        var groupSuggestions = await _db.StudyGroups.AsNoTracking()
            .Where(g => (g.Status == null || g.Status == "" || g.Status == "approved" || g.Status == "Approved" || g.Status == "active" || g.Status == "Active")
                && EF.Functions.Like(g.Name.ToLower(), $"%{q.ToLower()}%"))
            .OrderBy(g => g.Name)
            .Take(limit)
            .Select(g => new StudyGroupSearchSuggestionDto("group", g.Name, g.Id.ToString()))
            .ToListAsync(ct);
        suggestions.AddRange(groupSuggestions);

        if (suggestions.Count < limit)
        {
            var tagSuggestions = await _tagRepository.SearchTagsAsync(q);
            suggestions.AddRange(tagSuggestions
                .Where(t => t.Id.HasValue && !string.IsNullOrWhiteSpace(t.Name))
                .Take(limit - suggestions.Count)
                .Select(t => new StudyGroupSearchSuggestionDto("tag", t.Name!, t.Id!.Value.ToString())));
        }

        return new StudyGroupSearchSuggestionsResponse(suggestions.Take(limit).ToList());
    }

    public async Task<StudyGroupRecommendationResponse> GetRelatedAsync(
        Guid groupId,
        int limit,
        bool excludeJoined,
        string? userId,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 12);
        var tagsByGroup = await GetTagIdsByGroupAsync(new[] { groupId }, ct);
        tagsByGroup.TryGetValue(groupId, out var sourceTags);

        var query = new StudyGroupRecommendationQuery
        {
            Page = 1,
            PageSize = limit,
            Scene = "detailRelated",
            SortMode = "relevance",
            ExcludeJoined = excludeJoined,
            TagIds = sourceTags == null ? null : string.Join(",", sourceTags)
        };

        var response = await GetRecommendationsAsync(query, userId, ct);
        var filtered = response.Items
            .Where(x => x.Id != groupId)
            .Take(limit)
            .ToList();

        return response with
        {
            Total = filtered.Count,
            Items = filtered
        };
    }

    public async Task RecordFeedbackAsync(
        StudyGroupRecommendationFeedbackRequest request,
        string? userId,
        CancellationToken ct = default)
    {
        if (request.GroupId == Guid.Empty || string.IsNullOrWhiteSpace(request.Action))
        {
            return;
        }

        var action = NormalizeFeedbackAction(request.Action);
        if (action == null)
        {
            return;
        }

        _db.StudyGroupRecommendationFeedback.Add(new StudyGroupRecommendationFeedback
        {
            RequestId = request.RequestId,
            UserId = string.IsNullOrWhiteSpace(userId) ? null : userId,
            GroupId = request.GroupId,
            Action = action,
            Scene = NormalizeToken(request.Scene, "discover"),
            Position = request.Position,
            CreatedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // Feedback should not break the user-facing interaction.
        }
    }

    private async Task<UserInterestProfile> BuildUserProfileAsync(string? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return UserInterestProfile.Empty;
        }

        var memberships = await _db.UserGroups.AsNoTracking()
            .Where(x => x.UserId == userId && (x.Status == null || x.Status == "" || x.Status == "Active" || x.Status == "active"))
            .Select(x => new { x.GroupId, x.Role })
            .ToListAsync(ct);

        var joinedGroupIds = memberships.Select(x => x.GroupId).Distinct().ToHashSet();
        var personalGroupIds = await _db.UserGroups.AsNoTracking()
            .Where(x => x.UserId == userId && (x.Status == null || x.Status == "" || x.Status == "Active" || x.Status == "active"))
            .Join(_db.Groups.AsNoTracking().Where(g => g.Kind == "PersonalGroup"),
                ug => ug.GroupId,
                g => g.Id,
                (ug, g) => ug.GroupId)
            .ToListAsync(ct);

        var joinedStudyGroupIds = await _db.StudyGroups.AsNoTracking()
            .Where(g => joinedGroupIds.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync(ct);

        var tagIds = new HashSet<Guid>();
        var tagsByGroup = await GetTagIdsByGroupAsync(joinedStudyGroupIds, ct);
        foreach (var id in tagsByGroup.Values.SelectMany(x => x))
        {
            tagIds.Add(id);
        }

        var activePlanStableIds = await _db.GroupPlanEnrollments.AsNoTracking()
            .Where(x => personalGroupIds.Contains(x.GroupId) && x.Status == "Active")
            .Select(x => x.StudyPlanStableId)
            .Distinct()
            .ToListAsync(ct);

        var negativeGroupIds = new List<Guid>();
        try
        {
            negativeGroupIds = await _db.StudyGroupRecommendationFeedback.AsNoTracking()
                .Where(x => x.UserId == userId && (x.Action == "not_interested" || x.Action == "hide"))
                .OrderByDescending(x => x.CreatedAt)
                .Take(500)
                .Select(x => x.GroupId)
                .ToListAsync(ct);
        }
        catch
        {
            // Feedback tables are introduced by the discovery migration. Keep
            // recommendations available if an environment has not applied it yet.
        }

        var favoriteAndLearnedTagIds = await GetFavoriteAndLearnedTagIdsAsync(personalGroupIds, ct);
        foreach (var id in favoriteAndLearnedTagIds)
        {
            tagIds.Add(id);
        }

        return new UserInterestProfile(
            userId,
            joinedGroupIds,
            tagIds,
            activePlanStableIds.ToHashSet(),
            negativeGroupIds.ToHashSet());
    }

    private async Task<List<Guid>> RetrieveCandidateIdsAsync(
        string? query,
        IReadOnlyCollection<Guid> filterTagIds,
        UserInterestProfile profile,
        string sortMode,
        int take,
        CancellationToken ct)
    {
        var candidateIds = new List<Guid>();
        var seen = new HashSet<Guid>();

        async Task AddAsync(IQueryable<Guid> source)
        {
            var rows = await source.Take(take).ToListAsync(ct);
            foreach (var id in rows)
            {
                if (seen.Add(id))
                {
                    candidateIds.Add(id);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.ToLower();
            await AddAsync(_db.StudyGroups.AsNoTracking()
                .Where(g => (g.Status == null || g.Status == "" || g.Status == "approved" || g.Status == "Approved" || g.Status == "active" || g.Status == "Active")
                    && (EF.Functions.Like(g.Name.ToLower(), $"%{q}%")
                        || (g.Description != null && EF.Functions.Like(g.Description.ToLower(), $"%{q}%"))))
                .OrderByDescending(g => g.CreatedAt)
                .Select(g => g.Id));
        }

        var tagRecallIds = filterTagIds.Count > 0
            ? filterTagIds
            : profile.TagIds;
        if (tagRecallIds.Count > 0)
        {
            foreach (var id in await GetGroupIdsByTagsAsync(tagRecallIds, take, ct))
            {
                if (seen.Add(id))
                {
                    candidateIds.Add(id);
                }
            }
        }

        if (profile.ActivePlanStableIds.Count > 0)
        {
            await AddAsync(_db.StudyGroupStudyPlans.AsNoTracking()
                .Where(x => profile.ActivePlanStableIds.Contains(x.StudyPlanStableId))
                .OrderByDescending(x => x.UpdatedDate)
                .Select(x => x.StudyGroupId));
        }

        if (profile.JoinedGroupIds.Count > 0)
        {
            var joinedGroupTags = profile.TagIds;
            if (joinedGroupTags.Count > 0)
            {
                foreach (var id in await GetGroupIdsByTagsAsync(joinedGroupTags, take, ct))
                {
                    if (seen.Add(id))
                    {
                        candidateIds.Add(id);
                    }
                }
            }
        }

        var fallbackOrder = sortMode == "popular"
            ? _db.StudyGroups.AsNoTracking()
                .Where(g => g.Status == null || g.Status == "" || g.Status == "approved" || g.Status == "Approved" || g.Status == "active" || g.Status == "Active")
                .GroupJoin(_db.UserGroups.AsNoTracking().Where(ug => ug.Status == null || ug.Status == "" || ug.Status == "Active" || ug.Status == "active"),
                    g => g.Id,
                    ug => ug.GroupId,
                    (g, members) => new { g.Id, MemberCount = members.Count(), g.CreatedAt })
                .OrderByDescending(x => x.MemberCount)
                .ThenByDescending(x => x.CreatedAt)
                .Select(x => x.Id)
            : _db.StudyGroups.AsNoTracking()
                .Where(g => g.Status == null || g.Status == "" || g.Status == "approved" || g.Status == "Approved" || g.Status == "active" || g.Status == "Active")
                .OrderByDescending(g => g.CreatedAt)
                .Select(g => g.Id);

        await AddAsync(fallbackOrder);
        return candidateIds.Take(take).ToList();
    }

    private async Task<List<StudyGroupRecommendationItemDto>> BuildRankedItemsAsync(
        IReadOnlyList<Guid> candidateIds,
        UserInterestProfile profile,
        string? query,
        IReadOnlyCollection<Guid> filterTagIds,
        string scene,
        string sortMode,
        bool excludeJoined,
        bool excludeApplied,
        CancellationToken ct)
    {
        if (candidateIds.Count == 0)
        {
            return new List<StudyGroupRecommendationItemDto>();
        }

        var candidates = await _db.StudyGroups.AsNoTracking()
            .Where(sg => candidateIds.Contains(sg.Id)
                && (sg.Status == null || sg.Status == "" || sg.Status == "approved" || sg.Status == "Approved" || sg.Status == "active" || sg.Status == "Active"))
            .GroupJoin(_db.Groups.AsNoTracking(),
                sg => sg.Id,
                g => g.Id,
                (sg, groups) => new { StudyGroup = sg, Groups = groups })
            .SelectMany(
                x => x.Groups.DefaultIfEmpty(),
                (x, g) => new
                {
                    x.StudyGroup.Id,
                    x.StudyGroup.Name,
                    x.StudyGroup.Description,
                    x.StudyGroup.ImageUrl,
                    x.StudyGroup.Visibility,
                    x.StudyGroup.JoinPolicy,
                    x.StudyGroup.Status,
                    x.StudyGroup.CreatedAt,
                    GroupUpdatedAt = g == null ? (DateTimeOffset?)null : g.UpdatedAt
                })
            .ToListAsync(ct);

        var candidateIdSet = candidates.Select(x => x.Id).ToList();
        var recentMembershipCutoff = DateTimeOffset.UtcNow.AddDays(-7);
        var members = await _db.UserGroups.AsNoTracking()
            .Where(x => candidateIdSet.Contains(x.GroupId))
            .GroupBy(x => x.GroupId)
            .Select(g => new
            {
                GroupId = g.Key,
                MemberCount = g.Count(x => x.Status == null || x.Status == "" || x.Status == "Active" || x.Status == "active"),
                ActiveMemberCount7d = g.Count(x => (x.Status == null || x.Status == "" || x.Status == "Active" || x.Status == "active")
                    && x.JoinedAt >= recentMembershipCutoff)
            })
            .ToDictionaryAsync(x => x.GroupId, x => x, ct);

        var tagsByGroup = await GetTagIdsByGroupAsync(candidateIdSet, ct);
        var allTagIds = tagsByGroup.Values.SelectMany(x => x).Distinct().ToList();
        var tagNames = await _tagRepository.GetTagNamesAsync(allTagIds);

        var sharedPlansByGroup = await _db.StudyGroupStudyPlans.AsNoTracking()
            .Where(x => candidateIdSet.Contains(x.StudyGroupId))
            .GroupBy(x => x.StudyGroupId)
            .Select(g => new
            {
                GroupId = g.Key,
                PlanIds = g.Select(x => x.StudyPlanStableId).Distinct().ToList()
            })
            .ToDictionaryAsync(x => x.GroupId, x => x.PlanIds, ct);

        var appliedGroupIds = await GetAppliedGroupIdsAsync(profile, candidateIdSet, ct);
        var ranked = new List<StudyGroupRecommendationItemDto>();
        var now = DateTimeOffset.UtcNow;

        foreach (var candidate in candidates)
        {
            if (excludeJoined && profile.JoinedGroupIds.Contains(candidate.Id))
            {
                continue;
            }
            if (excludeApplied && appliedGroupIds.Contains(candidate.Id))
            {
                continue;
            }
            if (!CanSee(candidate.Visibility, profile.JoinedGroupIds.Contains(candidate.Id), scene))
            {
                continue;
            }

            tagsByGroup.TryGetValue(candidate.Id, out var groupTagIds);
            groupTagIds ??= new List<Guid>();
            sharedPlansByGroup.TryGetValue(candidate.Id, out var groupPlanIds);
            groupPlanIds ??= new List<Guid>();

            var matchedTagIds = groupTagIds
                .Where(id => profile.TagIds.Contains(id) || filterTagIds.Contains(id))
                .Distinct()
                .ToList();
            var matchedPlanIds = groupPlanIds
                .Where(id => profile.ActivePlanStableIds.Contains(id))
                .Distinct()
                .ToList();

            var tagScore = groupTagIds.Count == 0
                ? 0
                : matchedTagIds.Count / (double)Math.Min(Math.Max(profile.TagIds.Count + filterTagIds.Count, 1), groupTagIds.Count);
            var planScore = groupPlanIds.Count == 0 ? 0 : matchedPlanIds.Count / (double)groupPlanIds.Count;
            var textScore = GetTextScore(query, candidate.Name, candidate.Description);
            var freshnessScore = GetFreshnessScore(candidate.GroupUpdatedAt ?? candidate.CreatedAt);
            var qualityScore = GetQualityScore(candidate.Description, candidate.ImageUrl, groupTagIds.Count);
            var memberCount = members.TryGetValue(candidate.Id, out var memberInfo) ? memberInfo.MemberCount : 0;
            var activeMemberCount7d = memberInfo?.ActiveMemberCount7d ?? 0;
            var activityScore = Math.Min(1, Math.Log10(memberCount + 1) / 2.0);

            var score = sortMode switch
            {
                "latest" => freshnessScore,
                "popular" => activityScore * 0.7 + freshnessScore * 0.3,
                "relevance" => textScore * 0.35 + tagScore * 0.35 + planScore * 0.15 + freshnessScore * 0.10 + qualityScore * 0.05,
                _ => tagScore * 0.35 + planScore * 0.20 + textScore * 0.15 + freshnessScore * 0.15 + activityScore * 0.10 + qualityScore * 0.05
            };

            if (profile.NegativeGroupIds.Contains(candidate.Id))
            {
                score -= 1;
            }

            var reasons = BuildReasons(matchedTagIds, matchedPlanIds, textScore, freshnessScore, activityScore, tagNames);
            var tagDtos = groupTagIds
                .Select(id => new TagDTO { Id = id, Name = tagNames.TryGetValue(id, out var name) ? name : id.ToString() })
                .ToList();

            ranked.Add(new StudyGroupRecommendationItemDto(
                candidate.Id,
                candidate.Name,
                candidate.Description,
                candidate.ImageUrl,
                candidate.Visibility,
                candidate.JoinPolicy,
                candidate.Status,
                memberCount,
                activeMemberCount7d,
                candidate.CreatedAt,
                candidate.GroupUpdatedAt,
                profile.JoinedGroupIds.Contains(candidate.Id),
                appliedGroupIds.Contains(candidate.Id),
                tagDtos,
                new StudyGroupRecommendationMetaDto(
                    Math.Round(Math.Max(0, score), 4),
                    reasons,
                    matchedTagIds,
                    matchedPlanIds,
                    Array.Empty<Guid>())));
        }

        return ranked
            .OrderByDescending(x => x.Recommendation.Score)
            .ThenByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .ToList();
    }

    private async Task<HashSet<Guid>> GetAppliedGroupIdsAsync(UserInterestProfile profile, IReadOnlyList<Guid> groupIds, CancellationToken ct)
    {
        if (groupIds.Count == 0 || string.IsNullOrWhiteSpace(profile.UserId))
        {
            return new HashSet<Guid>();
        }

        var groupIdStrings = groupIds.Select(x => x.ToString()).ToList();
        await using var session = _neo4jDriver.AsyncSession();
        var rows = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(@"
UNWIND $groupIds AS groupId
MATCH (u:User {id: $userId})
WITH u, groupId
OPTIONAL MATCH (u)-[r:APPLIED_TO]->(s:StudyGroup {id: groupId})
WHERE coalesce(r.status, 'Pending') = 'Pending'
WITH groupId, count(r) AS appliedCount
WHERE appliedCount > 0
RETURN groupId
", new { groupIds = groupIdStrings, userId = profile.UserId });
            return await cursor.ToListAsync(record => record["groupId"].As<string>());
        });

        return rows
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .ToHashSet();
    }

    private async Task<Dictionary<Guid, List<Guid>>> GetTagIdsByGroupAsync(IEnumerable<Guid> groupIds, CancellationToken ct)
    {
        var ids = groupIds.Where(x => x != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, List<Guid>>();
        }

        await using var session = _neo4jDriver.AsyncSession();
        var rows = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(@"
UNWIND $groupIds AS groupId
OPTIONAL MATCH (t:Tag)-[:TAGGED_WITH]->(s:StudyGroup {id: groupId})
RETURN groupId, collect(DISTINCT t.id) AS tagIds
", new { groupIds = ids.Select(x => x.ToString()).ToList() });
            return await cursor.ToListAsync(record => new
            {
                GroupId = record["groupId"].As<string>(),
                TagIds = record["tagIds"].As<List<string?>>()
            });
        });

        return rows
            .Where(x => Guid.TryParse(x.GroupId, out _))
            .ToDictionary(
                x => Guid.Parse(x.GroupId),
                x => x.TagIds
                    .Where(t => Guid.TryParse(t, out _))
                    .Select(t => Guid.Parse(t!))
                    .Distinct()
                    .ToList());
    }

    private async Task<IReadOnlyList<Guid>> GetGroupIdsByTagsAsync(IEnumerable<Guid> tagIds, int take, CancellationToken ct)
    {
        var ids = tagIds.Where(x => x != Guid.Empty).Distinct().Take(50).ToList();
        if (ids.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        await using var session = _neo4jDriver.AsyncSession();
        var rows = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(@"
UNWIND $tagIds AS tagId
MATCH (:Tag {id: tagId})-[:TAGGED_WITH]->(s:StudyGroup)
RETURN s.id AS groupId, count(*) AS hits
ORDER BY hits DESC
LIMIT $take
", new { tagIds = ids.Select(x => x.ToString()).ToList(), take });
            return await cursor.ToListAsync(record => record["groupId"].As<string?>());
        });

        return rows
            .Where(x => Guid.TryParse(x, out _))
            .Select(x => Guid.Parse(x!))
            .Distinct()
            .ToList();
    }

    private async Task<IReadOnlyList<Guid>> GetFavoriteAndLearnedTagIdsAsync(IEnumerable<Guid> personalGroupIds, CancellationToken ct)
    {
        var groupIds = personalGroupIds.Where(x => x != Guid.Empty).Select(x => x.ToString()).Distinct().ToList();
        if (groupIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        await using var session = _neo4jDriver.AsyncSession();
        var rows = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(@"
UNWIND $groupIds AS groupId
MATCH (:Group {id: groupId})-[:OWNS]->(:Favorite)-[:INCLUDES]->(n:KnowledgeNode)
OPTIONAL MATCH (t:Tag)-[:TAGGED_WITH]->(n)
RETURN DISTINCT t.id AS tagId
", new { groupIds });
            return await cursor.ToListAsync(record => record["tagId"].As<string?>());
        });

        return rows
            .Where(x => Guid.TryParse(x, out _))
            .Select(x => Guid.Parse(x!))
            .Distinct()
            .ToList();
    }

    private async Task LogRequestAsync(
        Guid requestId,
        string? userId,
        string scene,
        string sortMode,
        string? query,
        int candidateCount,
        int returnedCount,
        CancellationToken ct)
    {
        _db.StudyGroupRecommendationRequestLogs.Add(new StudyGroupRecommendationRequestLog
        {
            RequestId = requestId,
            UserId = string.IsNullOrWhiteSpace(userId) ? null : userId,
            Scene = scene,
            SortMode = sortMode,
            Query = query,
            StrategyVersion = StrategyVersion,
            CandidateCount = candidateCount,
            ReturnedCount = returnedCount,
            CreatedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // Request logging is observational; recommendation responses should survive logging failures.
        }
    }

    private static List<StudyGroupRecommendationReasonDto> BuildReasons(
        IReadOnlyList<Guid> matchedTagIds,
        IReadOnlyList<Guid> matchedPlanIds,
        double textScore,
        double freshnessScore,
        double activityScore,
        IReadOnlyDictionary<Guid, string> tagNames)
    {
        var reasons = new List<StudyGroupRecommendationReasonDto>();
        var firstTag = matchedTagIds.FirstOrDefault();
        if (firstTag != Guid.Empty)
        {
            var tagName = tagNames.TryGetValue(firstTag, out var name) ? name : firstTag.ToString();
            reasons.Add(new StudyGroupRecommendationReasonDto("tag_match", $"因为你对「{tagName}」感兴趣", 0.35));
        }

        if (matchedPlanIds.Count > 0)
        {
            reasons.Add(new StudyGroupRecommendationReasonDto("active_plan_match", "该小组正在学习与你相关的学习计划", 0.20));
        }

        if (textScore > 0.4)
        {
            reasons.Add(new StudyGroupRecommendationReasonDto("text_match", "与你搜索的关键词相关", 0.20));
        }

        if (freshnessScore > 0.7)
        {
            reasons.Add(new StudyGroupRecommendationReasonDto("fresh_update", "最近更新活跃", 0.15));
        }

        if (reasons.Count == 0 && activityScore > 0.3)
        {
            reasons.Add(new StudyGroupRecommendationReasonDto("popular_group", "近期较多学习者参与", 0.10));
        }

        if (reasons.Count == 0)
        {
            reasons.Add(new StudyGroupRecommendationReasonDto("discovery_fallback", "适合继续探索的学习小组", 0.05));
        }

        return reasons.Take(3).ToList();
    }

    private static double GetTextScore(string? query, string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        var q = query.Trim().ToLower();
        var haystack = $"{name} {description}".ToLower();
        if (haystack.Contains(q))
        {
            return name.ToLower().Contains(q) ? 1 : 0.7;
        }

        var tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return 0;
        }

        return tokens.Count(haystack.Contains) / (double)tokens.Length;
    }

    private static double GetFreshnessScore(DateTimeOffset timestamp)
    {
        var days = Math.Max(0, (DateTimeOffset.UtcNow - timestamp).TotalDays);
        return Math.Exp(-days / 30.0);
    }

    private static double GetQualityScore(string? description, string? imageUrl, int tagCount)
    {
        var score = 0.2;
        if (!string.IsNullOrWhiteSpace(description) && description.Length >= 40) score += 0.35;
        if (!string.IsNullOrWhiteSpace(imageUrl)) score += 0.2;
        if (tagCount > 0) score += Math.Min(0.25, tagCount * 0.05);
        return Math.Min(1, score);
    }

    private static bool CanSee(string? visibility, bool isMember, string scene)
    {
        if (string.Equals(visibility, "Private", StringComparison.OrdinalIgnoreCase))
        {
            return isMember;
        }

        if (string.Equals(visibility, "Unlisted", StringComparison.OrdinalIgnoreCase))
        {
            return isMember || string.Equals(scene, "detailRelated", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static bool IsVisibleStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status)
            || status.Equals("approved", StringComparison.OrdinalIgnoreCase)
            || status.Equals("active", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActiveMembershipStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status)
            || status.Equals("Active", StringComparison.OrdinalIgnoreCase);
    }

    private static List<Guid> ParseGuidCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new List<Guid>();
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => Guid.TryParse(x, out _))
            .Select(Guid.Parse)
            .Distinct()
            .ToList();
    }

    private static string? NormalizeQuery(string? value)
    {
        var q = value?.Trim();
        return string.IsNullOrWhiteSpace(q) ? null : q;
    }

    private static string NormalizeToken(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }

    private static string NormalizeSortMode(string? value, string fallback)
    {
        var normalized = NormalizeToken(value, fallback);
        return normalized.Equals("active", StringComparison.OrdinalIgnoreCase)
            ? "popular"
            : normalized;
    }

    private static string? NormalizeFeedbackAction(string action)
    {
        var normalized = action.Trim().ToLowerInvariant();
        return normalized is "impression" or "click" or "apply_join" or "join_success" or "not_interested" or "hide"
            ? normalized
            : null;
    }

    private sealed record UserInterestProfile(
        string? UserId,
        HashSet<Guid> JoinedGroupIds,
        HashSet<Guid> TagIds,
        HashSet<Guid> ActivePlanStableIds,
        HashSet<Guid> NegativeGroupIds)
    {
        public static readonly UserInterestProfile Empty = new(
            null,
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>(),
            new HashSet<Guid>());

        public bool HasSignals => JoinedGroupIds.Count > 0 || TagIds.Count > 0 || ActivePlanStableIds.Count > 0;
    }
}
