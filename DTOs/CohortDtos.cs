namespace Sciencetopia.DTOs;

public record CohortCreateDto(string? Title, string? Visibility, DateTime? StartAt, DateTime? EndAt);
public record CohortUpdateDto(string? Title, string? Visibility, DateTime? StartAt, DateTime? EndAt);
public record CohortViewDto(
    Guid Id,
    Guid StudyPlanStableId,
    string? Title,
    string? Visibility,
    DateTime? StartAt,
    DateTime? EndAt,
    string? CreatedBy,
    DateTime CreatedAt,
    Guid? StudyPlanId = null,
    Guid? StudyGroupId = null,
    int? PinnedVersionNumber = null,
    string? EnrollMode = null,
    int? MemberCount = null);
// Removed CohortEnrollRequest (deprecated)
