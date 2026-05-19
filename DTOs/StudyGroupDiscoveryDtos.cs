namespace Sciencetopia.DTOs;

public sealed class StudyGroupRecommendationQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string Scene { get; set; } = "discover";
    public string SortMode { get; set; } = "personalized";
    public bool ExcludeJoined { get; set; } = true;
    public bool ExcludeApplied { get; set; } = false;
    public string? TagIds { get; set; }
    public string? Q { get; set; }
}

public sealed record StudyGroupRecommendationResponse(
    Guid RequestId,
    string StrategyVersion,
    int Page,
    int PageSize,
    int Total,
    bool IsPersonalized,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<StudyGroupRecommendationItemDto> Items);

public sealed record StudyGroupRecommendationItemDto(
    Guid Id,
    string Name,
    string? Description,
    string? ImageUrl,
    string Visibility,
    string JoinPolicy,
    string? Status,
    int MemberCount,
    int ActiveMemberCount7d,
    DateTime CreatedAt,
    DateTimeOffset? UpdatedAt,
    bool IsMember,
    bool HasApplied,
    IReadOnlyList<TagDTO> Tags,
    StudyGroupRecommendationMetaDto Recommendation);

public sealed record StudyGroupRecommendationMetaDto(
    double Score,
    IReadOnlyList<StudyGroupRecommendationReasonDto> Reasons,
    IReadOnlyList<Guid> MatchedTagIds,
    IReadOnlyList<Guid> MatchedPlanIds,
    IReadOnlyList<Guid> MatchedGroupIds);

public sealed record StudyGroupRecommendationReasonDto(
    string Code,
    string Text,
    double Weight);

public sealed record StudyGroupSearchSuggestionsResponse(
    IReadOnlyList<StudyGroupSearchSuggestionDto> Items);

public sealed record StudyGroupSearchSuggestionDto(
    string Type,
    string Value,
    string? Id = null);

public sealed class StudyGroupRecommendationFeedbackRequest
{
    public Guid RequestId { get; set; }
    public Guid GroupId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Scene { get; set; } = "discover";
    public int? Position { get; set; }
}
