using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Sciencetopia.Models;
using Sciencetopia.Data;

public interface INodeApprovalRepository
{
    Task<bool> ApproveNodeAsync(Guid nodeId, string reviewerId);
    Task<bool> DisapproveNodeAsync(string nodeId);
    Task<bool> ResubmitNodeAsync(string nodeId);
    Task<List<KnowledgeNodeDraft>> GetPendingNodesAsync();
    Task<List<KnowledgeNodeDraft>> GetPendingNodesByUserIdAsync(string userId);
    Task<List<TagDraft>> GetPendingTagsAsync();
    Task<List<TagDraft>> GetPendingTagsByUserIdAsync(string userId);
    Task<Guid> CreateDraftAsync(CreateNodeRequest request, string userId);
    Task<Guid> CreateNodeEditDraftAsync(EditNodeRequest request, string userId);
    Task<int> CountApprovedNodeDraftsAsync(string userId);
}

public class NodeApprovalRepository : INodeApprovalRepository
{
    private readonly ApplicationDbContext _context;

    public NodeApprovalRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> ApproveNodeAsync(Guid nodeId, string reviewerId)
    {
        // 获取最新的 pending 草稿
        var draft = await _context.KnowledgeNodeDrafts
            .Where(d => d.NodeId == nodeId && d.ReviewStatus == ReviewStatus.Pending)
            .OrderByDescending(d => d.SubmittedAt)
            .FirstOrDefaultAsync();

        if (draft == null)
            return false;

        var node = await _context.KnowledgeNodes.FindAsync(nodeId);

        if (node == null)
        {
            // 不存在，则创建新节点
            _context.KnowledgeNodes.Add(new KnowledgeNode
            {
                Id = draft.NodeId,
                Name = draft.Name,
                Description = draft.Description,
                CreatedDate = draft.SubmittedAt,
                UpdatedDate = DateTimeOffset.UtcNow
            });
        }
        else
        {
            // 获取当前最大版本号（为空时从 0 开始）
            var lastVersion = await _context.KnowledgeNodeVersions
                .Where(v => v.NodeId == nodeId)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync();

            int newVersionNumber = lastVersion?.VersionNumber + 1 ?? 1;

            // ✅ 保存旧版本
            _context.KnowledgeNodeVersions.Add(new KnowledgeNodeVersion
            {
                Id = Guid.NewGuid(),
                NodeId = node.Id ?? throw new InvalidOperationException("Node ID cannot be null"),
                Name = node.Name,
                Description = node.Description,
                CreatedDate = DateTimeOffset.UtcNow,
                PublishedBy = reviewerId,
                PublishedAt = DateTimeOffset.UtcNow,
                VersionNumber = newVersionNumber
            });

            // 已存在，则更新原有节点
            node.Name = draft.Name;
            node.Description = draft.Description;
            node.UpdatedDate = DateTimeOffset.UtcNow;
        }

        // 审核通过该草稿
        draft.ReviewStatus = ReviewStatus.Approved;
        draft.ReviewedAt = DateTimeOffset.UtcNow;

        var otherDrafts = await _context.KnowledgeNodeDrafts
            .Where(d => d.NodeId == nodeId && d.Id != draft.Id && d.ReviewStatus == ReviewStatus.Pending)
            .ToListAsync();

        foreach (var other in otherDrafts)
        {
            other.ReviewStatus = ReviewStatus.Rejected;
            other.ReviewedAt = DateTimeOffset.UtcNow;
            other.ReviewedBy = reviewerId;
        }

        return await _context.SaveChangesAsync() > 0;
    }

    public async Task<bool> DisapproveNodeAsync(string nodeId)
    {
        var drafts = await _context.KnowledgeNodeDrafts
            .Where(d => d.NodeId.ToString() == nodeId && d.ReviewStatus == ReviewStatus.Pending)
            .ToListAsync();

        foreach (var draft in drafts)
        {
            draft.ReviewStatus = ReviewStatus.Rejected;
            draft.ReviewedAt = DateTimeOffset.UtcNow;
        }

        return await _context.SaveChangesAsync() > 0;
    }

    public async Task<bool> ResubmitNodeAsync(string nodeId)
    {
        var rejected = await _context.KnowledgeNodeDrafts
            .FirstOrDefaultAsync(d => d.NodeId.ToString() == nodeId && d.ReviewStatus == ReviewStatus.Rejected);

        if (rejected != null)
        {
            rejected.ReviewStatus = ReviewStatus.Pending;
            rejected.SubmittedAt = DateTimeOffset.UtcNow;
            return await _context.SaveChangesAsync() > 0;
        }

        return false;
    }

    public async Task<List<KnowledgeNodeDraft>> GetPendingNodesAsync()
    {
        return await _context.KnowledgeNodeDrafts
            .Where(d => d.ReviewStatus == ReviewStatus.Pending)
            .OrderByDescending(d => d.SubmittedAt)
            .ToListAsync();
    }

    public async Task<List<TagDraft>> GetPendingTagsAsync()
    {
        return await _context.TagDrafts
            .Where(d => d.ReviewStatus == ReviewStatus.Pending)
            .OrderByDescending(d => d.SubmittedAt)
            .ToListAsync();
    }

    public async Task<List<KnowledgeNodeDraft>> GetPendingNodesByUserIdAsync(string userId)
    {
        return await _context.KnowledgeNodeDrafts
            .Where(d => d.ReviewStatus == ReviewStatus.Pending && d.SubmittedBy == userId)
            .OrderByDescending(d => d.SubmittedAt)
            .ToListAsync();
    }

    public async Task<List<TagDraft>> GetPendingTagsByUserIdAsync(string userId)
    {
        return await _context.TagDrafts
            .Where(d => d.ReviewStatus == ReviewStatus.Pending && d.SubmittedBy == userId)
            .OrderByDescending(d => d.SubmittedAt)
            .ToListAsync();
    }

    public async Task<Guid> CreateDraftAsync(CreateNodeRequest request, string userId)
    {
        var nodeId = Guid.NewGuid();

        var draft = new KnowledgeNodeDraft
        {
            Id = Guid.NewGuid(),
            NodeId = nodeId,
            Name = request.Name,
            Description = request.Description,
            SubmittedBy = userId,
            SubmittedAt = DateTimeOffset.UtcNow,
            ReviewStatus = ReviewStatus.Pending
        };

        _context.KnowledgeNodeDrafts.Add(draft);
        await _context.SaveChangesAsync();

        return nodeId;
    }

    public async Task<Guid> CreateNodeEditDraftAsync(EditNodeRequest request, string userId)
    {
        var draftId = Guid.NewGuid();

        var draft = new KnowledgeNodeDraft
        {
            Id = draftId,
            NodeId = request.NodeId,
            Name = request.Name,
            Description = request.Description,
            SubmittedBy = userId,
            SubmittedAt = DateTimeOffset.UtcNow,
            ReviewStatus = ReviewStatus.Pending
        };

        await _context.KnowledgeNodeDrafts.AddAsync(draft);
        return draftId;
    }

    public async Task<int> CountApprovedNodeDraftsAsync(string userId)
    {
        // 获取所有该用户提交的、审核通过的草稿
        var grouped = await _context.KnowledgeNodeDrafts
            .Where(d => d.ReviewStatus == ReviewStatus.Approved && d.SubmittedBy == userId)
            .GroupBy(d => d.NodeId)
            .Select(g => g.OrderByDescending(d => d.SubmittedAt).FirstOrDefault())
            .ToListAsync();

        // 如果你只想统计其中确实是“最新状态为Approved”的草稿（防止老草稿干扰）
        var latestApprovedCount = grouped.Count(d => d != null && d.ReviewStatus == ReviewStatus.Approved);

        return latestApprovedCount;
    }
}