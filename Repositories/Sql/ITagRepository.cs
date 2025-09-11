using Sciencetopia.Data;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Models;

public interface ITagRepository
{
    Task<IEnumerable<Guid>> GetTagNodeIdsByTagTypeAsync(string tagType);
    Task<List<Tags>> GetAllTagsAsync();
    Task<List<string>> GetAllTagSystemsAsync();
    Task<List<TagDTO>> GetTagsByNameAsync(IEnumerable<string> inputTagNames);
    Task<List<TagDTO>> SearchTagsAsync(string query);
    Task<List<string>> SearchTagNamesAsync(string query);
    Task<Dictionary<Guid, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>> GetTagDetailsAsync(IEnumerable<Guid> ids);
    Task<Dictionary<Guid, string>> GetTagNamesAsync(IEnumerable<Guid> ids, string language = "zh");
    Dictionary<string, (string Name, string Description, DateTime CreatedDate, DateTime UpdatedDate)> GetRepresentativeNodes(IEnumerable<string> tagIds);
    Task<Guid> CreateIfNotExistsAsync(string tagName);
    Task<Guid> CreateTagDraftAsync(string name, string? description, string submittedBy);
    Task<bool> ApproveTagDraftIfPendingAsync(Guid tagId, string reviewerId);
}

public class TagRepository : ITagRepository
{
    private readonly ApplicationDbContext _context;
    private readonly Sciencetopia.Services.L10n.IL10nService _l10n;
    private readonly Microsoft.Extensions.Options.IOptions<Sciencetopia.Services.L10n.L10nOptions> _l10nOptions;

    public TagRepository(ApplicationDbContext context,
                         Sciencetopia.Services.L10n.IL10nService l10n,
                         Microsoft.Extensions.Options.IOptions<Sciencetopia.Services.L10n.L10nOptions> l10nOptions)
    {
        _context = context;
        _l10n = l10n;
        _l10nOptions = l10nOptions;
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

    public async Task<List<Tags>> GetAllTagsAsync()
    {
        return await _context.Tags
                             .Select(tag => new Tags
                             {
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
            return new List<TagDTO>();
        }

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

    public async Task<List<string>> SearchTagNamesAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<string>();
        }

        query = query.ToLower();

        var tagNames = await _context.Tags
            .Where(t => t.Name != null && EF.Functions.Like(t.Name.ToLower(), $"%{query}%"))
            .Select(t => t.Name)
            .ToListAsync();

        return tagNames;
    }

    public async Task<List<string>> GetAllTagSystemsAsync()
    {
        return await _context.TypesOfTags
            .Where(t => t.Type != null)
            .Select(t => t.Type!)
            .Distinct()
            .ToListAsync();
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

    public async Task<Dictionary<Guid, string>> GetTagNamesAsync(IEnumerable<Guid> ids, string language = "zh")
    {
        if (_l10nOptions.Value.Enabled)
        {
            var dict = new Dictionary<Guid, string>();
            foreach (var id in ids.Distinct())
            {
                var title = await _l10n.GetLocalizedForTagAsync(id, "title", language);
                dict[id] = string.IsNullOrWhiteSpace(title) ? id.ToString() : title!;
            }
            return dict;
        }
        // fallback to base field only
        var idSet = ids.ToHashSet();
        var rows = await _context.Tags
                         .Where(t => t.Id.HasValue && idSet.Contains(t.Id.Value))
                         .Select(t => new { Id = t.Id!.Value, Name = t.Name })
                         .ToListAsync();
        return rows.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First().Name ?? string.Empty);
    }

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
        _context.Tags.Add(newTag);
        await _context.SaveChangesAsync();
        return newTag.Id ?? throw new InvalidOperationException("The new tag ID is null.");
    }

    public async Task<Guid> CreateTagDraftAsync(string name, string? description, string submittedBy)
    {
        var existingDraft = await _context.TagDrafts
            .FirstOrDefaultAsync(td =>
                td.Name == name &&
                td.SubmittedBy == submittedBy &&
                td.ReviewStatus == ReviewStatus.Pending);

        if (existingDraft != null)
        {
            return existingDraft.TagId != Guid.Empty ? existingDraft.TagId : Guid.Empty;
        }

        var tagId = Guid.NewGuid();

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
