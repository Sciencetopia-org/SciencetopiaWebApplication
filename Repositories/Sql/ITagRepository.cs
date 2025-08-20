using Sciencetopia.Data;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Models;

public interface ITagRepository
{
    Task<IEnumerable<Guid>> GetTagNodeIdsByTagTypeAsync(string tagType);
    Task<List<Tags>> GetAllTagsAsync();
    Task<List<TagDTO>> GetTagsByNameAsync(IEnumerable<string> inputTagNames);
    Task<List<TagDTO>> SearchTagsAsync(string query);
    Task<Dictionary<Guid, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>> GetTagDetailsAsync(IEnumerable<Guid> ids);
    Task<Dictionary<Guid, string>> GetTagNamesAsync(IEnumerable<Guid> ids);
    Dictionary<string, (string Name, string Description, DateTime CreatedDate, DateTime UpdatedDate)> GetRepresentativeNodes(IEnumerable<string> tagIds);
    Task<Guid> CreateIfNotExistsAsync(string tagName);
    Task<Guid> CreateTagDraftAsync(string name, string? description, string submittedBy);
    Task<bool> ApproveTagDraftIfPendingAsync(Guid tagId, string reviewerId);
}

public class TagRepository : ITagRepository
{
    private readonly ApplicationDbContext _context;

    public TagRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Guid>> GetTagNodeIdsByTagTypeAsync(string tagType)
    {
        return await (from tag in _context.Tags
                      join tagTypeEntity in _context.TagTypes on tag.Id equals tagTypeEntity.TagId
                      join typeOfTag in _context.TypesOfTags on tagTypeEntity.TypeId equals typeOfTag.Id
                      where typeOfTag.Type == tagType && tag.Id.HasValue
                      select tag.Id.Value)
                 .ToListAsync();
    }

    // 获取所有Tag信息，转换Tag.Id为字符串
    public async Task<List<Tags>> GetAllTagsAsync()
    {
        return await _context.Tags
                             .Select(tag => new Tags
                             {
                                 // 将 Guid 转换为字符串
                                 Id = tag.Id,
                                 Name = tag.Name,
                                 Description = tag.Description,
                                 CreatedDate = tag.CreatedDate.HasValue ? tag.CreatedDate.Value.UtcDateTime : default,
                                 UpdatedDate = tag.UpdatedDate.HasValue ? tag.UpdatedDate.Value.UtcDateTime : default
                             })
                             .ToListAsync();
    }

    public async Task<List<TagDTO>> GetTagsByNameAsync(IEnumerable<string> inputTagNames)
    {
        if (inputTagNames == null || !inputTagNames.Any())
        {
            return new List<TagDTO>(); // 输入为空，返回空列表
        }

        // 使用 EF Core 查询匹配的标签
        var tags = await _context.Tags
            .Where(t => inputTagNames.Contains(t.Name))
            .Select(t => new TagDTO
            {
                Id = t.Id,
                Name = t.Name
            })
            .ToListAsync();

        return tags;
    }

    public async Task<List<TagDTO>> SearchTagsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<TagDTO>();
        }

        query = query.ToLower();

        var tags = await _context.Tags
            .Where(t => t.Name != null && EF.Functions.Like(t.Name.ToLower(), $"%{query}%"))
            .Select(t => new TagDTO
            {
                Id = t.Id,
                Name = t.Name
            })
            .ToListAsync();

        return tags;
    }

    public async Task<Dictionary<Guid, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>> GetTagDetailsAsync(IEnumerable<Guid> ids)
    {
        var tags = await _context.Tags
                            .Where(tag => ids.Contains(tag.Id.Value))
                            .Select(tag => new
                            {
                                Id = tag.Id.Value,
                                tag.Name,
                                tag.Description,
                                // 将 DateTime 转换为 DateTimeOffset（假设这里的 DateTime 为本地时间，可以根据实际情况调整）
                                CreatedDate = tag.CreatedDate,
                                UpdatedDate = tag.UpdatedDate
                            })
                            .ToListAsync();

        var dict = new Dictionary<Guid, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>();
        foreach (var tag in tags)
        {
            dict[tag.Id] = (tag.Name ?? string.Empty, tag.Description ?? string.Empty, tag.CreatedDate.HasValue ? tag.CreatedDate.Value : default, tag.UpdatedDate.HasValue ? tag.UpdatedDate.Value : default);
        }
        return dict;
    }

    public async Task<Dictionary<Guid, string>> GetTagNamesAsync(IEnumerable<Guid> ids)
    {
        const int batchSize = 20;
        var idList = ids.ToList();
        var dict = new Dictionary<Guid, string>();

        for (int i = 0; i < idList.Count; i += batchSize)
        {
            var batch = idList.Skip(i).Take(batchSize).ToList();

            var tagNames = await _context.Tags
                .Where(tag => tag.Id.HasValue && batch.Contains(tag.Id.Value))
                .Select(tag => new { tag.Id, tag.Name })
                .ToListAsync(); // 保持 IQueryable 流程

            foreach (var tag in tagNames)
            {
                if (tag.Id.HasValue)
                    dict[tag.Id.Value] = tag.Name ?? string.Empty;
            }
        }

        return dict;
    }

    // 获取标签的代表节点；注意：此处 tagIds 为字符串，需要将 tag.Id 转换为字符串进行比较
    public Dictionary<string, (string Name, string Description, DateTime CreatedDate, DateTime UpdatedDate)> GetRepresentativeNodes(IEnumerable<string> tagIds)
    {
        var tagNameDict = _context.Tags
                                .Where(tag => tag.Id != null && tagIds.Contains(tag.Id.ToString()))
                                .ToDictionary(tag => tag.Id.ToString()!, tag => tag.Name);

        var nodes = _context.KnowledgeNodes
                    .Where(node => tagNameDict.Values.Contains(node.Name))
                    .ToList();

        var dict = new Dictionary<string, (string Name, string Description, DateTime CreatedDate, DateTime UpdatedDate)>();
        foreach (var node in nodes)
        {
            if (node.Id != null)
            {
                dict[node.Id.ToString()] = (node.Name ?? string.Empty, node.Description ?? string.Empty, node.CreatedDate.HasValue ? node.CreatedDate.Value.UtcDateTime : default, node.UpdatedDate.HasValue ? node.UpdatedDate.Value.UtcDateTime : default);
            }
        }
        return dict;
    }

    public async Task<Guid> CreateIfNotExistsAsync(string tagName)
    {
        var tag = await _context.Tags.FirstOrDefaultAsync(t => t.Name == tagName);
        if (tag != null && tag.Id.HasValue) return tag.Id.Value;

        var newTag = new Tags
        {
            Name = tagName,
            CreatedDate = DateTimeOffset.UtcNow,
            UpdatedDate = DateTimeOffset.UtcNow
        };
        // Id 是 Guid 类型，EF Core 会自动生成
        _context.Tags.Add(newTag);
        await _context.SaveChangesAsync();
        return newTag.Id ?? throw new InvalidOperationException("The new tag ID is null.");
    }

    public async Task<Guid> CreateTagDraftAsync(string name, string? description, string submittedBy)
    {
        // 是否存在同名、未审的草稿（可避免重复提交）
        var existingDraft = await _context.TagDrafts
            .FirstOrDefaultAsync(td =>
                td.Name == name &&
                td.SubmittedBy == submittedBy &&
                td.ReviewStatus == ReviewStatus.Pending);

        if (existingDraft != null)
        {
            return existingDraft.TagId != Guid.Empty ? existingDraft.TagId : Guid.Empty; // 返回已存在的草稿 ID
        }

        var tagId = Guid.NewGuid(); // 生成新的 TagId

        var draft = new TagDraft
        {
            Id = Guid.NewGuid(),
            TagId = tagId,
            Name = name,
            Description = description,
            SubmittedBy = submittedBy,
            SubmittedAt = DateTimeOffset.UtcNow,
            ReviewStatus = ReviewStatus.Pending,
            ReviewedBy = null,
            ReviewedAt = null,
            ReviewComment = null
        };

        _context.TagDrafts.Add(draft);
        await _context.SaveChangesAsync();

        return tagId;
    }

    public async Task<bool> ApproveTagDraftIfPendingAsync(Guid tagId, string reviewerId)
    {
        var draft = await _context.TagDrafts
            .Where(d => d.TagId == tagId && d.ReviewStatus == ReviewStatus.Pending)
            .OrderByDescending(d => d.SubmittedAt)
            .FirstOrDefaultAsync();

        if (draft == null)
            return false;

        var exists = await _context.Tags.AnyAsync(t => t.Id == tagId);
        if (!exists)
        {
            _context.Tags.Add(new Tags
            {
                Id = tagId,
                Name = draft.Name,
                Description = draft.Description,
                CreatedDate = draft.SubmittedAt
            });
        }

        draft.ReviewStatus = ReviewStatus.Approved;
        draft.ReviewedAt = DateTimeOffset.UtcNow;
        draft.ReviewedBy = reviewerId;

        await _context.SaveChangesAsync();
        return true;
    }
}