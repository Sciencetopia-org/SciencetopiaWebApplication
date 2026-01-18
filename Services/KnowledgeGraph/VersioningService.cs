using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;

namespace Sciencetopia.Services.KnowledgeGraph;

public interface IVersioningService
{
    Task<Guid> CreateNodeDraftAsync(Guid stableId, Action<KnowledgeNode> mutate, string userId, CancellationToken ct = default);
    Task<Guid> CreateTagDraftAsync(Guid stableId, Action<Tags> mutate, string userId, CancellationToken ct = default);
    Task PublishNodeAsync(Guid draftVersionId, string approverId, CancellationToken ct = default);
    Task PublishTagAsync(Guid draftVersionId, string approverId, CancellationToken ct = default);
    Task ArchiveNodeAsync(Guid versionId, string approverId, CancellationToken ct = default);
    Task ArchiveTagAsync(Guid versionId, string approverId, CancellationToken ct = default);
    Task<IReadOnlyList<KnowledgeNode>> GetNodeVersionsAsync(Guid stableId, CancellationToken ct = default);
    Task<IReadOnlyList<Tags>> GetTagVersionsAsync(Guid stableId, CancellationToken ct = default);
    Task RejectNodeAsync(Guid versionId, string approverId, CancellationToken ct = default);
    Task RejectTagAsync(Guid versionId, string approverId, CancellationToken ct = default);
}

public class VersioningService : IVersioningService
{
    private readonly ApplicationDbContext _db;

    public VersioningService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid> CreateNodeDraftAsync(Guid stableId, Action<KnowledgeNode> mutate, string userId, CancellationToken ct = default)
    {
        if (mutate is null) throw new ArgumentNullException(nameof(mutate));
        if (userId is null) throw new ArgumentNullException(nameof(userId));

        var normalizedStableId = stableId == Guid.Empty ? Guid.NewGuid() : stableId;
        var maxVersion = await _db.KnowledgeNodes
            .Where(x => x.StableId == normalizedStableId)
            .MaxAsync(x => (int?)x.VersionNumber, ct) ?? 0;

        var now = DateTimeOffset.UtcNow;
        var draft = new KnowledgeNode
        {
            Id = Guid.NewGuid(),
            StableId = normalizedStableId,
            VersionNumber = maxVersion + 1,
            Status = "Draft",
            IsCurrent = false,
            CreatedAt = now,
            CreatedBy = userId,
            PublishedAt = null,
            RetiredAt = null,
            ApprovedAt = null,
            ApprovedBy = null
        };

        mutate(draft);
        _db.KnowledgeNodes.Add(draft);
        await _db.SaveChangesAsync(ct);
        return draft.Id!.Value;
    }

    public async Task<Guid> CreateTagDraftAsync(Guid stableId, Action<Tags> mutate, string userId, CancellationToken ct = default)
    {
        if (mutate is null) throw new ArgumentNullException(nameof(mutate));
        if (userId is null) throw new ArgumentNullException(nameof(userId));

        var normalizedStableId = stableId == Guid.Empty ? Guid.NewGuid() : stableId;
        var maxVersion = await _db.Tags
            .Where(x => x.StableId == normalizedStableId)
            .MaxAsync(x => (int?)x.VersionNumber, ct) ?? 0;

        var now = DateTimeOffset.UtcNow;
        var draft = new Tags
        {
            Id = Guid.NewGuid(),
            StableId = normalizedStableId,
            VersionNumber = maxVersion + 1,
            Status = "Draft",
            IsCurrent = false,
            CreatedAt = now,
            CreatedBy = userId,
            PublishedAt = null,
            RetiredAt = null,
            ApprovedAt = null,
            ApprovedBy = null
        };

        mutate(draft);
        _db.Tags.Add(draft);
        await _db.SaveChangesAsync(ct);
        return draft.Id!.Value;
    }

    public async Task PublishNodeAsync(Guid draftVersionId, string approverId, CancellationToken ct = default)
    {
        if (approverId is null) throw new ArgumentNullException(nameof(approverId));

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var draft = await _db.KnowledgeNodes.SingleAsync(x => x.Id == draftVersionId, ct);
        if (!string.Equals(draft.Status, "Draft", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only Draft node versions can be published.");
        }

        var now = DateTimeOffset.UtcNow;
        var currentVersions = await _db.KnowledgeNodes
            .Where(x => x.StableId == draft.StableId && x.IsCurrent)
            .ToListAsync(ct);

        foreach (var existing in currentVersions)
        {
            existing.IsCurrent = false;
            existing.Status = "Archived";
            existing.RetiredAt = now;
            existing.ApprovedAt ??= now;
            existing.ApprovedBy ??= approverId;
        }

        draft.Status = "Current";
        draft.IsCurrent = true;
        draft.PublishedAt = now;
        draft.ApprovedAt = now;
        draft.ApprovedBy = approverId;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task PublishTagAsync(Guid draftVersionId, string approverId, CancellationToken ct = default)
    {
        if (approverId is null) throw new ArgumentNullException(nameof(approverId));

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var draft = await _db.Tags.SingleAsync(x => x.Id == draftVersionId, ct);
        if (!string.Equals(draft.Status, "Draft", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only Draft tag versions can be published.");
        }

        var now = DateTimeOffset.UtcNow;
        var currentVersions = await _db.Tags
            .Where(x => x.StableId == draft.StableId && x.IsCurrent)
            .ToListAsync(ct);

        foreach (var existing in currentVersions)
        {
            existing.IsCurrent = false;
            existing.Status = "Archived";
            existing.RetiredAt = now;
            existing.ApprovedAt ??= now;
            existing.ApprovedBy ??= approverId;
        }

        draft.Status = "Current";
        draft.IsCurrent = true;
        draft.PublishedAt = now;
        draft.ApprovedAt = now;
        draft.ApprovedBy = approverId;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ArchiveNodeAsync(Guid versionId, string approverId, CancellationToken ct = default)
    {
        if (approverId is null) throw new ArgumentNullException(nameof(approverId));
        var node = await _db.KnowledgeNodes.SingleAsync(x => x.Id == versionId, ct);
        var now = DateTimeOffset.UtcNow;
        node.IsCurrent = false;
        node.Status = "Archived";
        node.RetiredAt = now;
        node.ApprovedAt = now;
        node.ApprovedBy = approverId;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ArchiveTagAsync(Guid versionId, string approverId, CancellationToken ct = default)
    {
        if (approverId is null) throw new ArgumentNullException(nameof(approverId));
        var tag = await _db.Tags.SingleAsync(x => x.Id == versionId, ct);
        var now = DateTimeOffset.UtcNow;
        tag.IsCurrent = false;
        tag.Status = "Archived";
        tag.RetiredAt = now;
        tag.ApprovedAt = now;
        tag.ApprovedBy = approverId;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<KnowledgeNode>> GetNodeVersionsAsync(Guid stableId, CancellationToken ct = default)
    {
        return await _db.KnowledgeNodes
            .Where(x => x.StableId == stableId)
            .OrderByDescending(x => x.VersionNumber)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Tags>> GetTagVersionsAsync(Guid stableId, CancellationToken ct = default)
    {
        return await _db.Tags
            .Where(x => x.StableId == stableId)
            .OrderByDescending(x => x.VersionNumber)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task RejectNodeAsync(Guid versionId, string approverId, CancellationToken ct = default)
    {
        if (approverId is null) throw new ArgumentNullException(nameof(approverId));
        var node = await _db.KnowledgeNodes.SingleAsync(x => x.Id == versionId, ct);
        var now = DateTimeOffset.UtcNow;
        node.IsCurrent = false;
        node.Status = "Rejected";
        node.RetiredAt = now;
        node.ApprovedAt = now;
        node.ApprovedBy = approverId;
        await _db.SaveChangesAsync(ct);
    }

    public async Task RejectTagAsync(Guid versionId, string approverId, CancellationToken ct = default)
    {
        if (approverId is null) throw new ArgumentNullException(nameof(approverId));
        var tag = await _db.Tags.SingleAsync(x => x.Id == versionId, ct);
        var now = DateTimeOffset.UtcNow;
        tag.IsCurrent = false;
        tag.Status = "Rejected";
        tag.RetiredAt = now;
        tag.ApprovedAt = now;
        tag.ApprovedBy = approverId;
        await _db.SaveChangesAsync(ct);
    }
}
