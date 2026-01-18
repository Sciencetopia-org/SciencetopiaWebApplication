using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.DTOs.KnowledgeGraphDTOs;

namespace Sciencetopia.Services.KnowledgeGraph;

public interface IKnowledgeGraphWorkflowService
{
    Task<NodeDraftResult> CreateNodeDraftAsync(CreateNodeRequest request, string userId, CancellationToken ct = default);
    Task<NodeDraftResult> CreateNodeEditDraftAsync(EditNodeRequest request, string userId, CancellationToken ct = default);
    Task PublishNodeAsync(Guid versionId, string reviewerId, CancellationToken ct = default);
    Task RejectNodeAsync(Guid versionId, string reviewerId, CancellationToken ct = default);
    Task<IReadOnlyList<PendingNodeSummary>> GetPendingNodeDraftsAsync(CancellationToken ct = default);

    Task<TagDraftResult> CreateTagDraftAsync(string name, string? description, string userId, CancellationToken ct = default);
    Task PublishTagAsync(Guid versionId, string reviewerId, CancellationToken ct = default);
    Task RejectTagAsync(Guid versionId, string reviewerId, CancellationToken ct = default);
    Task<IReadOnlyList<PendingTagSummary>> GetPendingTagDraftsAsync(CancellationToken ct = default);
}

public sealed class KnowledgeGraphWorkflowService : IKnowledgeGraphWorkflowService
{
    private readonly ApplicationDbContext _db;
    private readonly IVersioningService _versioning;
    private readonly IGraphRepository _graphRepository;
    public KnowledgeGraphWorkflowService(
        ApplicationDbContext db,
        IVersioningService versioning,
        IGraphRepository graphRepository)
    {
        _db = db;
        _versioning = versioning;
        _graphRepository = graphRepository;
    }

    public async Task<NodeDraftResult> CreateNodeDraftAsync(CreateNodeRequest request, string userId, CancellationToken ct = default)
    {
        var stableId = Guid.NewGuid();
        var versionId = await _versioning.CreateNodeDraftAsync(stableId, node =>
        {
            node.Name = request.Name ?? string.Empty;
            node.Description = request.Description;
            node.CreatedBy = userId;
        }, userId, ct);
        return new NodeDraftResult(stableId, versionId);
    }

    public async Task<NodeDraftResult> CreateNodeEditDraftAsync(EditNodeRequest request, string userId, CancellationToken ct = default)
    {
        var stableId = request.NodeId;
        if (stableId == Guid.Empty) throw new ArgumentException("NodeId is required", nameof(request));
        var versionId = await _versioning.CreateNodeDraftAsync(stableId, node =>
        {
            node.Name = request.Name ?? string.Empty;
            node.Description = request.Description;
            node.CreatedBy = userId;
        }, userId, ct);
        return new NodeDraftResult(stableId, versionId);
    }

    public async Task PublishNodeAsync(Guid versionId, string reviewerId, CancellationToken ct = default)
    {
        await _versioning.PublishNodeAsync(versionId, reviewerId, ct);
    }

    public async Task RejectNodeAsync(Guid versionId, string reviewerId, CancellationToken ct = default)
    {
        await _versioning.RejectNodeAsync(versionId, reviewerId, ct);
    }

    public async Task<IReadOnlyList<PendingNodeSummary>> GetPendingNodeDraftsAsync(CancellationToken ct = default)
    {
        return await _db.KnowledgeNodes
            .AsNoTracking()
            .Where(x => x.Status == "Draft" && !x.IsCurrent)
            .OrderByDescending(x => x.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(x => new PendingNodeSummary(
                x.StableId,
                x.Id!.Value,
                x.Name ?? string.Empty,
                x.Description,
                x.CreatedAt,
                x.CreatedBy
            ))
            .ToListAsync(ct);
    }

    public async Task<TagDraftResult> CreateTagDraftAsync(string name, string? description, string userId, CancellationToken ct = default)
    {
        var stableId = Guid.NewGuid();
        var versionId = await _versioning.CreateTagDraftAsync(stableId, tag =>
        {
            tag.Name = name;
            tag.Description = description;
            tag.CreatedBy = userId;
        }, userId, ct);
        return new TagDraftResult(stableId, versionId);
    }

    public async Task PublishTagAsync(Guid versionId, string reviewerId, CancellationToken ct = default)
    {
        await _versioning.PublishTagAsync(versionId, reviewerId, ct);
    }

    public async Task RejectTagAsync(Guid versionId, string reviewerId, CancellationToken ct = default)
    {
        await _versioning.RejectTagAsync(versionId, reviewerId, ct);
    }

    public async Task<IReadOnlyList<PendingTagSummary>> GetPendingTagDraftsAsync(CancellationToken ct = default)
    {
        return await _db.Tags
            .AsNoTracking()
            .Where(x => x.Status == "Draft" && !x.IsCurrent)
            .OrderByDescending(x => x.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(x => new PendingTagSummary(
                x.StableId,
                x.Id!.Value,
                x.Name ?? string.Empty,
                x.Description,
                x.CreatedAt,
                x.CreatedBy
            ))
            .ToListAsync(ct);
    }
}
