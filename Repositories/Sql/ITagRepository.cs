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
    Task<Dictionary<Guid, Guid>> GetRepresentativeNodeIdsAsync(IEnumerable<Guid> tagIds);
    // Fully replace legacy name-based approach: now from TagRepresentativeNode table
    Task<Dictionary<Guid, (Guid NodeId, string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>> GetRepresentativeNodesAsync(IEnumerable<Guid> tagIds, string language = "zh");
    Task<Guid> CreateIfNotExistsAsync(string tagName);
}

public class TagRepository : ITagRepository
{
    private readonly ApplicationDbContext _context;
    private readonly Sciencetopia.Services.L10n.IL10nService _l10n;
    private readonly Microsoft.Extensions.Options.IOptions<Sciencetopia.Services.L10n.L10nOptions> _l10nOptions;
    private readonly Sciencetopia.Middleware.ILanguageContext _langCtx;

    private IQueryable<Tags> ActiveTags => _context.Tags.Where(t => t.IsCurrent && t.Status == "Current");

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
        return await (
            from tagTypeRel in _context.TagTypes
            join type in _context.TypesOfTags on tagTypeRel.TypeId equals type.Id
            join tag in ActiveTags on tagTypeRel.TagStableId equals tag.StableId
            where type.Type == tagType
            select tag.StableId
        )
        .Distinct()
        .ToListAsync();
    }

    public async Task<List<Tags>> GetAllTagsAsync()
    {
        var list = await ActiveTags
            .AsNoTracking()
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
                from t in ActiveTags
                where t.Id.HasValue
                join tls in _context.TagL10nSets on t.Id!.Value equals tls.TagId
                join si in _context.L10nSetItems on tls.L10nSetId equals si.L10nSetId
                join i in _context.L10nItems on si.L10nItemId equals i.L10nItemId
                where i.FieldKey == "name" && (i.LangCode == lang || i.LangCode == null)
                select new { t.StableId, Name = i.Content ?? i.Text }
            ).ToListAsync();

            return rows
                .Where(r => !string.IsNullOrWhiteSpace(r.Name) && names.Contains(r.Name!.ToLower()))
                .Select(r => new TagDTO { Id = r.StableId, Name = r.Name })
                .ToList();
        }
        else
        {
            var tags = await ActiveTags
                .Where(t => t.Name != null && names.Contains(t.Name.ToLower()))
                .Select(t => new TagDTO { Id = t.StableId, Name = t.Name })
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
                from t in ActiveTags
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
            var tags = await ActiveTags
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
                from t in ActiveTags
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
            var tagNames = await ActiveTags
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

        var rows = await ActiveTags
            .Where(tag => tag.Id.HasValue && (idSet.Contains(tag.Id.Value) || idSet.Contains(tag.StableId)))
            .Select(tag => new
            {
                VersionId = tag.Id!.Value,
                tag.StableId,
                tag.Name,
                tag.Description,
                tag.CreatedAt,
                tag.PublishedAt,
                tag.ApprovedAt
            })
            .ToListAsync();

        var dict = rows.ToDictionary(
            x => idSet.Contains(x.StableId) ? x.StableId : x.VersionId,
            x => (
                (x.Name ?? string.Empty),
                (x.Description ?? string.Empty),
                x.CreatedAt ?? default,
                x.PublishedAt ?? x.ApprovedAt ?? x.CreatedAt ?? default
            )
        );

        if (_l10nOptions.Value.Enabled && dict.Count > 0)
        {
            var lang = _langCtx.EffectiveLang;
            var stableIds = rows.Select(x => x.StableId).Distinct().ToList();
            var nameMap = await _l10n.GetLocalizedForTagManyAsync(stableIds, "name", lang);
            var descMap = await _l10n.GetLocalizedForTagManyAsync(stableIds, "description", lang);
            foreach (var kv in dict.ToList())
            {
                var id = kv.Key;
                var tuple = kv.Value;
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

        var tagRows = await ActiveTags
            .AsNoTracking()
            .Where(t => t.Id.HasValue && (idSet.Contains(t.Id.Value) || idSet.Contains(t.StableId)))
            .Select(t => new { VersionId = t.Id!.Value, t.StableId, t.Name })
            .ToListAsync();

        var baseMap = new Dictionary<Guid, string>();
        foreach (var row in tagRows)
        {
            var name = row.Name ?? string.Empty;
            if (idSet.Contains(row.VersionId))
            {
                baseMap[row.VersionId] = name;
            }

            if (idSet.Contains(row.StableId))
            {
                baseMap[row.StableId] = name;
            }
        }

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

    public async Task<Dictionary<Guid, Guid>> GetRepresentativeNodeIdsAsync(IEnumerable<Guid> tagIds)
    {
        var idSet = tagIds?.ToHashSet() ?? new HashSet<Guid>();
        if (idSet.Count == 0) return new Dictionary<Guid, Guid>();

        var rows = await (
            from t in _context.Tags.AsNoTracking()
            where t.Id.HasValue
                  && t.IsCurrent
                  && t.Status == "Current"
                  && idSet.Contains(t.StableId)
            join tr in _context.TagRepresentativeNodes.AsNoTracking() on t.Id!.Value equals tr.TagId
            join n in _context.KnowledgeNodes.AsNoTracking() on tr.NodeId equals n.Id!.Value
            select new
            {
                TagStableId = t.StableId,
                NodeStableId = n.StableId
            })
            .ToListAsync();

        return rows
            .GroupBy(x => x.TagStableId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.NodeStableId).First());
    }


    public async Task<Dictionary<Guid, (Guid NodeId, string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>> GetRepresentativeNodesAsync(IEnumerable<Guid> tagIds, string language = "zh")
    {
        var result = new Dictionary<Guid, (Guid NodeId, string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>();
        var idSet = tagIds?.ToHashSet() ?? new HashSet<Guid>();
        if (idSet.Count == 0) return result;

        var tagMappings = await _context.Tags
            .AsNoTracking()
            .Where(t => t.Id.HasValue && (idSet.Contains(t.Id.Value) || idSet.Contains(t.StableId)))
            .Select(t => new { VersionId = t.Id!.Value, t.StableId })
            .ToListAsync();

        if (tagMappings.Count == 0) return result;

        var versionIdsForLookup = tagMappings.Select(m => m.VersionId).Distinct().ToList();
        var stableByVersion = tagMappings.ToDictionary(m => m.VersionId, m => m.StableId);

        // Step 1: Fetch TagVersionId -> NodeVersionId mapping only (avoid heavy join and tracking)
        var rawPairs = await _context.TagRepresentativeNodes
            .AsNoTracking()
            .Where(tr => versionIdsForLookup.Contains(tr.TagId))
            .Select(tr => new { tr.TagId, tr.NodeId })
            .ToListAsync();

        // If multiple representative nodes exist for a tag, prefer the first one
        var tagToNode = rawPairs
            .GroupBy(x => x.TagId)
            .ToDictionary(g => g.Key, g => g.First().NodeId);

        if (tagToNode.Count == 0) return result;

        var nodeVersionIds = tagToNode.Values.Distinct().ToList();

        // Step 2: Fetch base node metadata in a single, lean query
        var baseNodes = await _context.KnowledgeNodes
            .AsNoTracking()
            .Where(n => n.Id.HasValue && nodeVersionIds.Contains(n.Id.Value))
            .Select(n => new
            {
                Id = n.Id!.Value,
                n.StableId,
                Name = n.Name ?? string.Empty,
                Description = n.Description ?? string.Empty,
                CreatedAt = n.CreatedAt ?? default,
                UpdatedAt = n.PublishedAt ?? n.ApprovedAt ?? n.CreatedAt ?? default
            })
            .ToListAsync();

        var nodeMeta = baseNodes.ToDictionary(
            x => x.Id,
            x => (x.StableId, x.Name, x.Description, x.CreatedAt, x.UpdatedAt));

        // Step 3: L10n overlay if enabled
        Dictionary<Guid, string> nodeNameMap = new();
        Dictionary<Guid, string> nodeDescMap = new();
        var stableNodeIds = nodeMeta.Values.Select(x => x.StableId).Distinct().ToList();
        if (_l10nOptions.Value.Enabled && stableNodeIds.Count > 0)
        {
            var lang = string.IsNullOrWhiteSpace(language) ? _langCtx.EffectiveLang : language;
            nodeNameMap = await _l10n.GetLocalizedManyAsync(stableNodeIds, "name", lang);
            nodeDescMap = await _l10n.GetLocalizedManyAsync(stableNodeIds, "description", lang);
        }

        // Step 4: Build result dictionary
        foreach (var kv in tagToNode)
        {
            var tagVersionId = kv.Key;
            var tagStableId = stableByVersion.TryGetValue(tagVersionId, out var mappedStable) ? mappedStable : tagVersionId;
            var nodeVersionId = kv.Value;
            var meta = nodeMeta.TryGetValue(nodeVersionId, out var info)
                ? info
                : (StableId: Guid.Empty, Name: string.Empty, Description: string.Empty, CreatedAt: default(DateTimeOffset), UpdatedAt: default(DateTimeOffset));
            var stableNodeId = meta.StableId != Guid.Empty ? meta.StableId : nodeVersionId;
            var name = meta.Name;
            var desc = meta.Description;
            var created = meta.CreatedAt;
            var updated = meta.UpdatedAt;

            if (_l10nOptions.Value.Enabled)
            {
                if (nodeNameMap.TryGetValue(stableNodeId, out var nl) && !string.IsNullOrWhiteSpace(nl)) name = nl;
                if (nodeDescMap.TryGetValue(stableNodeId, out var dl) && !string.IsNullOrWhiteSpace(dl)) desc = dl;
            }

            result[tagStableId] = (stableNodeId, name, desc, created, updated);
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
                from t in ActiveTags
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
        var tag = await ActiveTags.FirstOrDefaultAsync(t => t.Name != null && t.Name.ToLower() == lower);
        if (tag != null && tag.Id.HasValue) return tag.Id.Value;

        var now = DateTimeOffset.UtcNow;
        var newTag = new Tags
        {
            Id = Guid.NewGuid(),
            StableId = Guid.NewGuid(),
            VersionNumber = 1,
            Status = "Current",
            IsCurrent = true,
            Name = tagName,
            CreatedBy = "system",
            CreatedAt = now,
            PublishedAt = now,
            ApprovedAt = now,
            ApprovedBy = "system"
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

}
