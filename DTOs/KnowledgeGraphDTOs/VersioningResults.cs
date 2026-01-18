namespace Sciencetopia.DTOs.KnowledgeGraphDTOs;

public readonly record struct NodeDraftResult(Guid StableId, Guid VersionId);
public readonly record struct TagDraftResult(Guid StableId, Guid VersionId);
public readonly record struct PendingNodeSummary(Guid StableId, Guid VersionId, string Name, string? Description, DateTimeOffset? SubmittedAt, string? SubmittedBy);
public readonly record struct PendingTagSummary(Guid StableId, Guid VersionId, string Name, string? Description, DateTimeOffset? SubmittedAt, string? SubmittedBy);
