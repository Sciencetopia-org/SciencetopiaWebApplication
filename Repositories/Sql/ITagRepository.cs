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
    // Fully replace legacy name-based approach: now from TagRepresentativeNode table
    Task<Dictionary<Guid, (Guid NodeId, string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>> GetRepresentativeNodesAsync(IEnumerable<Guid> tagIds);
    Task<Guid> CreateIfNotExistsAsync(string tagName);
    Task<Guid> CreateTagDraftAsync(string name, string? description, string submittedBy);
    Task<bool> ApproveTagDraftIfPendingAsync(Guid tagId, string reviewerId);
}

public class TagRepository : ITagRepository
{
    private readonly ApplicationDbContext _context;
    private readonly Sciencetopia.Services.L10n.IL10nService _l10n;
    private readonly Microsoft.Extensions.Options.IOptions<Sciencetopia.Services.L10n.L10nOptions> _l10nOptions;
    private readonly Sciencetopia.Middleware.ILanguageContext _langCtx;

    public TagRepository(ApplicationDbContext context,
                         Sciencetopia.Services.L10n.IL10nService l10n,
                         Microsoft.Extensions.Options.IOptions<Sciencetopia.Services.L10n.L10nOptions> l10nOptions,
                         Sciencetopia.Middleware.ILanguageContext langCtx)
    {
        _context = context;
        _l10n = l10n;
        _l10nOptions = l10nOptions;
        _langCtx = langCtx;
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
        var list = await _context.Tags
            .Select(tag => new Tags
            {
                Id = tag.Id,
                Name = tag.Name,
                Description = tag.Description,
                CreatedDate = tag.CreatedDate.HasValue ? tag.CreatedDate.Value.UtcDateTime : default,
                UpdatedDate = tag.UpdatedDate.HasValue ? tag.UpdatedDate.Value.UtcDateTime : default
            })
            .ToListAsync();

        if (_l10nOptions.Value.Enabled && list.Count > 0)
        {
            var idSet = list.Where(t => t.Id.HasValue).Select(t => t.Id!.Value).ToHashSet();
            if (idSet.Count > 0)
            {
                var lang = _langCtx.EffectiveLang;
                var nameMap = await _l10n.GetLocalizedForTagManyAsync(idSet, "name", lang);
                var descMap = await _l10n.GetLocalizedForTagManyAsync(idSet, "description", lang);
                foreach (var t in list)
                {
                    if (t.Id.HasValue)
                    {
                        if (nameMap.TryGetValue(t.Id.Value, out var n) && !string.IsNullOrWhiteSpace(n)) t.Name = n;
                        if (descMap.TryGetValue(t.Id.Value, out var d) && !string.IsNullOrWhiteSpace(d)) t.Description = d;
                    }
                }
            }
        }
        return list;
    }

    public async Task<List<TagDTO>> GetTagsByNameAsync(IEnumerable<string> inputTagNames)
    {
        if (inputTagNames == null || !inputTagNames.Any())
        {
            return new List<TagDTO>();
        }

        var names = inputTagNames.Select(s => s?.Trim().ToLower()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        if (names.Count == 0) return new List<TagDTO>();

        if (_l10nOptions.Value.Enabled)
        {
            var lang = _langCtx.EffectiveLang;
            var rows = await (
                from t in _context.Tags
                where t.Id.HasValue
                join tls in _context.TagL10nSets on t.Id!.Value equals tls.TagId
                join si in _context.L10nSetItems on tls.L10nSetId equals si.L10nSetId
                join i in _context.L10nItems on si.L10nItemId equals i.L10nItemId
                where i.FieldKey == "name" && (i.LangCode == lang || i.LangCode == null)
                select new { t.Id, Name = i.Content ?? i.Text }
            ).ToListAsync();

            return rows
                .Where(r => r.Id.HasValue && !string.IsNullOrWhiteSpace(r.Name) && names.Contains(r.Name!.ToLower()))
                .Select(r => new TagDTO { Id = r.Id, Name = r.Name })
                .ToList();
        }
        else
        {
            var tags = await _context.Tags
                .Where(t => t.Name != null && names.Contains(t.Name.ToLower()))
                .Select(t => new TagDTO { Id = t.Id, Name = t.Name })
                .ToListAsync();
            return tags;
        }
    }

    public async Task<List<TagDTO>> SearchTagsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<TagDTO>();
        }

        var q = query.ToLower();
        if (_l10nOptions.Value.Enabled)
        {
            var lang = _langCtx.EffectiveLang;
            var rows = await (
                from t in _context.Tags
                where t.Id.HasValue
                join tls in _context.TagL10nSets on t.Id!.Value equals tls.TagId
                join si in _context.L10nSetItems on tls.L10nSetId equals si.L10nSetId
                join i in _context.L10nItems on si.L10nItemId equals i.L10nItemId
                where i.FieldKey == "name" && (i.LangCode == lang || i.LangCode == null)
                      && (i.Text != null || i.Content != null)
                select new { t.Id, Name = i.Content ?? i.Text }
            ).ToListAsync();

            return rows
                .Where(r => r.Id.HasValue && !string.IsNullOrWhiteSpace(r.Name) && r.Name!.ToLower().Contains(q))
                .Select(r => new TagDTO { Id = r.Id, Name = r.Name })
                .ToList();
        }
        else
        {
            var tags = await _context.Tags
                .Where(t => t.Name != null && EF.Functions.Like(t.Name.ToLower(), $"%{q}%"))
                .Select(t => new TagDTO { Id = t.Id, Name = t.Name })
                .ToListAsync();
            return tags;
        }
    }

    public async Task<List<string>> SearchTagNamesAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<string>();
        }

        var q = query.ToLower();
        if (_l10nOptions.Value.Enabled)
        {
            var lang = _langCtx.EffectiveLang;
            var names = await (
                from t in _context.Tags
                where t.Id.HasValue
                join tls in _context.TagL10nSets on t.Id!.Value equals tls.TagId
                join si in _context.L10nSetItems on tls.L10nSetId equals si.L10nSetId
                join i in _context.L10nItems on si.L10nItemId equals i.L10nItemId
                where i.FieldKey == "name" && (i.LangCode == lang || i.LangCode == null)
                      && (i.Text != null || i.Content != null)
                select (i.Content ?? i.Text)
            ).ToListAsync();

            return names
                .Where(n => !string.IsNullOrWhiteSpace(n) && n!.ToLower().Contains(q))
                .Distinct()
                .ToList()!;
        }
        else
        {
            var tagNames = await _context.Tags
                .Where(t => t.Name != null && EF.Functions.Like(t.Name.ToLower(), $"%{q}%"))
                .Select(t => t.Name!)
                .ToListAsync();
            return tagNames;
        }
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
        var idSet = ids?.ToHashSet() ?? new HashSet<Guid>();
        if (idSet.Count == 0) return new Dictionary<Guid, (string, string, DateTimeOffset, DateTimeOffset)>();

        var rows = await _context.Tags
            .Where(tag => tag.Id.HasValue && idSet.Contains(tag.Id.Value))
            .Select(tag => new
            {
                Id = tag.Id!.Value,
                tag.Name,
                tag.Description,
                CreatedDate = tag.CreatedDate,
                UpdatedDate = tag.UpdatedDate
            })
            .ToListAsync();

        var dict = rows.ToDictionary(
            x => x.Id,
            x => (
                (x.Name ?? string.Empty),
                (x.Description ?? string.Empty),
                x.CreatedDate.HasValue ? x.CreatedDate.Value : default,
                x.UpdatedDate.HasValue ? x.UpdatedDate.Value : default
            )
        );

        if (_l10nOptions.Value.Enabled && dict.Count > 0)
        {
            var lang = _langCtx.EffectiveLang;
            var nameMap = await _l10n.GetLocalizedForTagManyAsync(idSet, "name", lang);
            var descMap = await _l10n.GetLocalizedForTagManyAsync(idSet, "description", lang);
            foreach (var id in idSet)
            {
                if (!dict.TryGetValue(id, out var tuple)) continue;
                var name = tuple.Item1;
                var desc = tuple.Item2;
                if (nameMap.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n)) name = n;
                if (descMap.TryGetValue(id, out var d) && !string.IsNullOrWhiteSpace(d)) desc = d;
                dict[id] = (name, desc, tuple.Item3, tuple.Item4);
            }
        }
        return dict;
    }

    public async Task<Dictionary<Guid, string>> GetTagNamesAsync(IEnumerable<Guid> ids, string language = "zh")
    {
        var idSet = ids?.ToHashSet() ?? new HashSet<Guid>();
        if (idSet.Count == 0) return new Dictionary<Guid, string>();

        // Base tag names once
        var baseRows = await _context.Tags
                              .Where(t => t.Id.HasValue && idSet.Contains(t.Id.Value))
                              .Select(t => new { Id = t.Id!.Value, Name = t.Name })
                              .ToListAsync();
        var baseMap = baseRows.ToDictionary(r => r.Id, r => r.Name ?? string.Empty);

        // Overlay L10n if enabled
        if (_l10nOptions.Value.Enabled)
        {
            var locMap = await _l10n.GetLocalizedForTagManyAsync(idSet, "name", language);
            foreach (var id in idSet)
            {
                if (locMap.TryGetValue(id, out var title) && !string.IsNullOrWhiteSpace(title))
                    baseMap[id] = title;
            }
        }
        return baseMap;
    }


    public async Task<Dictionary<Guid, (Guid NodeId, string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>> GetRepresentativeNodesAsync(IEnumerable<Guid> tagIds)
    {
        var result = new Dictionary<Guid, (Guid NodeId, string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>();
        var idSet = tagIds?.ToHashSet() ?? new HashSet<Guid>();
        if (idSet.Count == 0) return result;

        // Step 1: Fetch TagId -> NodeId mapping only (avoid heavy join and tracking)
        var rawPairs = await _context.TagRepresentativeNodes
            .AsNoTracking()
            .Where(tr => idSet.Contains(tr.TagId))
            .Select(tr => new { tr.TagId, tr.NodeId })
            .ToListAsync();

        // If multiple representative nodes exist for a tag, prefer the first one
        var tagToNode = rawPairs
            .GroupBy(x => x.TagId)
            .ToDictionary(g => g.Key, g => g.First().NodeId);

        if (tagToNode.Count == 0) return result;

        var nodeIds = tagToNode.Values.Distinct().ToList();

        // Step 2: Fetch base node metadata in a single, lean query
        var baseNodes = await _context.KnowledgeNodes
            .AsNoTracking()
            .Where(n => n.Id.HasValue && nodeIds.Contains(n.Id.Value))
            .Select(n => new
            {
                Id = n.Id!.Value,
                Name = n.Name ?? string.Empty,
                Description = n.Description ?? string.Empty,
                CreatedDate = n.CreatedDate ?? default,
                UpdatedDate = n.UpdatedDate ?? default
            })
            .ToListAsync();

        var nodeMeta = baseNodes.ToDictionary(x => x.Id, x => (x.Name, x.Description, x.CreatedDate, x.UpdatedDate));

        // Step 3: L10n overlay if enabled
        Dictionary<Guid, string> nodeNameMap = new();
        Dictionary<Guid, string> nodeDescMap = new();
        if (_l10nOptions.Value.Enabled && nodeIds.Count > 0)
        {
            var lang = _langCtx.EffectiveLang;
            nodeNameMap = await _l10n.GetLocalizedManyAsync(nodeIds, "name", lang);
            nodeDescMap = await _l10n.GetLocalizedManyAsync(nodeIds, "description", lang);
        }

        // Step 4: Build result dictionary
        foreach (var kv in tagToNode)
        {
            var tagId = kv.Key;
            var nodeId = kv.Value;
            var name = nodeMeta.TryGetValue(nodeId, out var m) ? m.Item1 : string.Empty;
            var desc = nodeMeta.TryGetValue(nodeId, out var m2) ? m2.Item2 : string.Empty;
            var created = nodeMeta.TryGetValue(nodeId, out var m3) ? m3.Item3 : default;
            var updated = nodeMeta.TryGetValue(nodeId, out var m4) ? m4.Item4 : default;

            if (_l10nOptions.Value.Enabled)
            {
                if (nodeNameMap.TryGetValue(nodeId, out var nl) && !string.IsNullOrWhiteSpace(nl)) name = nl;
                if (nodeDescMap.TryGetValue(nodeId, out var dl) && !string.IsNullOrWhiteSpace(dl)) desc = dl;
            }

            result[tagId] = (nodeId, name, desc, created, updated);
        }
        return result;
    }

    public async Task<Guid> CreateIfNotExistsAsync(string tagName)
    {
        var lower = (tagName ?? string.Empty).Trim().ToLower();
        if (string.IsNullOrWhiteSpace(lower)) throw new ArgumentException("tagName is empty");

        // Try match by L10n name in effective language first when enabled
        if (_l10nOptions.Value.Enabled)
        {
            var lang = _langCtx.EffectiveLang;
            var existing = await (
                from t in _context.Tags
                where t.Id.HasValue
                join tls in _context.TagL10nSets on t.Id!.Value equals tls.TagId
                join si in _context.L10nSetItems on tls.L10nSetId equals si.L10nSetId
                join i in _context.L10nItems on si.L10nItemId equals i.L10nItemId
                where i.FieldKey == "name" && (i.LangCode == lang || i.LangCode == null)
                select new { t.Id, Name = i.Content ?? i.Text }
            ).ToListAsync();
            var match = existing.FirstOrDefault(r => r.Id.HasValue && !string.IsNullOrWhiteSpace(r.Name) && r.Name!.ToLower() == lower);
            if (match?.Id != null) return match.Id.Value;
        }

        // Fallback to base table exact name
        var tag = await _context.Tags.FirstOrDefaultAsync(t => t.Name != null && t.Name.ToLower() == lower);
        if (tag != null && tag.Id.HasValue) return tag.Id.Value;

        var newTag = new Tags
        {
            Name = tagName,
            CreatedDate = DateTimeOffset.UtcNow,
            UpdatedDate = DateTimeOffset.UtcNow
        };
        _context.Tags.Add(newTag);
        await _context.SaveChangesAsync();

        if (newTag.Id.HasValue && _l10nOptions.Value.Enabled)
        {
            var useLang = _langCtx.EffectiveLang ?? "zh";
            await _l10n.UpsertForTagAsync(newTag.Id.Value, "name", useLang, tagName, false, true, 0);
        }
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
