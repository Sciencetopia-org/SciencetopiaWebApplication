using Sciencetopia.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

public interface IKnowledgeNodeRepository
{
    Task<IEnumerable<Guid>> GetAllNodeIdsAsync();
    Task<(string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)?> GetNodeDetailsByIdAsync(Guid id, string language = "zh");
    Task<Dictionary<Guid, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>> GetNodesDetailsAsync(IEnumerable<Guid> ids, string language = "zh");
    Task<Dictionary<Guid, string>> GetNodesNamesAsync(IEnumerable<Guid> ids, string language = "zh");
    Task<Dictionary<Guid, string>> GetCurrentNodeNamesByStableIdsAsync(IEnumerable<Guid> stableIds, string language = "zh");
    Task<List<KnowledgeNode>> SearchKnowledgeNodesAsync(string query, int skip, int take);
}

public class KnowledgeNodeRepository : IKnowledgeNodeRepository
{
    private readonly ApplicationDbContext _context;
    private readonly Sciencetopia.Middleware.ILanguageContext _langCtx;

    public KnowledgeNodeRepository(ApplicationDbContext context,
                                   Sciencetopia.Middleware.ILanguageContext langCtx)
    {
        _context = context;
        _langCtx = langCtx;
    }

    public async Task<IEnumerable<Guid>> GetAllNodeIdsAsync()
    {
        return await _context.KnowledgeNodes
                             .Where(node => node.IsCurrent && node.Status == "Current")
                             .Select(node => node.StableId)
                             .ToListAsync();
    }

    public async Task<(string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)?> GetNodeDetailsByIdAsync(Guid id, string language = "zh")
    {
        // Project only the columns we actually need to avoid schema drift (e.g., missing RetiredAt in older DBs)
        var row = await _context.KnowledgeNodes
            .Where(n => n.IsCurrent && n.Status == "Current" &&
                        ((n.Id.HasValue && n.Id.Value == id) || n.StableId == id))
            .OrderByDescending(n => n.VersionNumber)
            .Select(n => new {
                n.Id,
                n.StableId,
                n.Name,
                n.Description,
                n.CreatedAt,
                n.PublishedAt,
                n.ApprovedAt,
                n.DefaultL10nSetId
            })
            .FirstOrDefaultAsync();
        if (row == null) return null;

        // L10n-aware override if enabled
        var opts = _context.GetService<Microsoft.Extensions.Options.IOptions<Sciencetopia.Services.L10n.L10nOptions>>();
        if (opts.Value.Enabled)
        {
            var l10n = _context.GetService<Sciencetopia.Services.L10n.IL10nService>();
            var title = await l10n.GetLocalizedAsync(id, "name", language);
            var desc = await l10n.GetLocalizedAsync(id, "description", language);
            return (
                (title ?? row.Name) ?? string.Empty,
                (desc ?? row.Description) ?? string.Empty,
                row.CreatedAt ?? default,
                row.PublishedAt ?? row.ApprovedAt ?? row.CreatedAt ?? default
            );
        }

        return (
            row.Name ?? string.Empty,
            row.Description ?? string.Empty,
            row.CreatedAt ?? default,
            row.PublishedAt ?? row.ApprovedAt ?? row.CreatedAt ?? default
        );
    }

    public async Task<Dictionary<Guid, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>> GetNodesDetailsAsync(IEnumerable<Guid> ids, string language = "zh")
    {
        var dict = new Dictionary<Guid, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>();
        foreach (var id in ids)
        {
            var details = await GetNodeDetailsByIdAsync(id, language);
            if (details.HasValue)
            {
                dict[id] = details.Value;
            }
        }
        return dict;
    }

    public async Task<Dictionary<Guid, string>> GetNodesNamesAsync(IEnumerable<Guid> ids, string language = "zh")
    {
        var idSet = ids?.ToHashSet() ?? new HashSet<Guid>();
        if (idSet.Count == 0) return new Dictionary<Guid, string>();

        // Base names in one query
        var baseRows = await _context.KnowledgeNodes
                              .Where(n => n.IsCurrent && n.Status == "Current" &&
                                          ((n.Id.HasValue && idSet.Contains(n.Id.Value)) || idSet.Contains(n.StableId)))
                              .Select(n => new { n.Id, n.StableId, n.Name })
                              .ToListAsync();
        var baseMap = new Dictionary<Guid, string>();
        foreach (var row in baseRows)
        {
            if (row.Id.HasValue && idSet.Contains(row.Id.Value))
                baseMap[row.Id.Value] = row.Name ?? string.Empty;
            if (idSet.Contains(row.StableId))
                baseMap[row.StableId] = row.Name ?? string.Empty;
        }

        // If L10n enabled, try to bulk fetch localized titles and overlay
        var opts = _context.GetService<Microsoft.Extensions.Options.IOptions<Sciencetopia.Services.L10n.L10nOptions>>();
        if (opts.Value.Enabled)
        {
            var l10n = _context.GetService<Sciencetopia.Services.L10n.IL10nService>();
            var locMap = await l10n.GetLocalizedManyAsync(idSet, "name", language);
            foreach (var id in idSet)
            {
                if (locMap.TryGetValue(id, out var title) && !string.IsNullOrWhiteSpace(title))
                    baseMap[id] = title;
            }
        }
        return baseMap;
    }

    public async Task<Dictionary<Guid, string>> GetCurrentNodeNamesByStableIdsAsync(IEnumerable<Guid> stableIds, string language = "zh")
    {
        var idSet = stableIds?.ToHashSet() ?? new HashSet<Guid>();
        if (idSet.Count == 0) return new Dictionary<Guid, string>();

        var baseRows = await _context.KnowledgeNodes
            .AsNoTracking()
            .Where(n => n.IsCurrent && n.Status == "Current" && idSet.Contains(n.StableId))
            .Select(n => new { n.StableId, Name = n.Name ?? string.Empty, n.DefaultL10nSetId })
            .ToListAsync();

        var baseMap = baseRows.ToDictionary(x => x.StableId, x => x.Name);

        var opts = _context.GetService<Microsoft.Extensions.Options.IOptions<Sciencetopia.Services.L10n.L10nOptions>>();
        if (opts.Value.Enabled)
        {
            var lang = string.IsNullOrWhiteSpace(language) ? _langCtx.EffectiveLang : language;
            var setIdByStableId = baseRows
                .Where(x => x.DefaultL10nSetId.HasValue)
                .ToDictionary(x => x.StableId, x => x.DefaultL10nSetId!.Value);

            var setIds = setIdByStableId.Values.Distinct().ToList();
            if (setIds.Count > 0)
            {
                var items = await (
                    from si in _context.L10nSetItems.AsNoTracking()
                    join i in _context.L10nItems.AsNoTracking() on si.L10nItemId equals i.L10nItemId
                    where setIds.Contains(si.L10nSetId)
                          && i.FieldKey == "name"
                          && i.Kind == Sciencetopia.Models.L10n.L10nItemKind.Primary
                          && (i.LangCode == lang || i.LangCode == null)
                    select new
                    {
                        si.L10nSetId,
                        i.LangCode,
                        Text = i.Content ?? i.Text,
                        i.SortOrder
                    })
                    .ToListAsync();

                var bestBySetId = items
                    .GroupBy(x => x.L10nSetId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderBy(x => x.LangCode == lang ? 0 : 1)
                              .ThenBy(x => x.SortOrder)
                              .Select(x => x.Text)
                              .FirstOrDefault());

                foreach (var (stableId, setId) in setIdByStableId)
                {
                    if (bestBySetId.TryGetValue(setId, out var title) && !string.IsNullOrWhiteSpace(title))
                    {
                        baseMap[stableId] = title!;
                    }
                }
            }
        }

        return baseMap;
    }

    public async Task<List<KnowledgeNode>> SearchKnowledgeNodesAsync(string query, int skip, int take)
    {
        var q = (query ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(q))
        {
            return new List<KnowledgeNode>();
        }

        // If L10n is enabled, search localized name/description first, falling back to base columns
        var opts = _context.GetService<Microsoft.Extensions.Options.IOptions<Sciencetopia.Services.L10n.L10nOptions>>();
        if (opts.Value.Enabled)
        {
            var lang = _langCtx.EffectiveLang;

            // Find nodes whose L10n name/description matches, in preferred lang or null
            var l10nMatches = await (
                from n in _context.KnowledgeNodes
                where n.Id.HasValue && n.IsCurrent && n.Status == "Current"
                join nls in _context.NodeL10nSets on n.Id!.Value equals nls.NodeId
                join si in _context.L10nSetItems on nls.L10nSetId equals si.L10nSetId
                join i in _context.L10nItems on si.L10nItemId equals i.L10nItemId
                where (i.FieldKey == "name" || i.FieldKey == "description")
                      && (i.LangCode == lang || i.LangCode == null)
                      && ((i.Text != null && EF.Functions.Like(i.Text, $"%{q}%"))
                          || (i.Content != null && EF.Functions.Like(i.Content, $"%{q}%")))
                select n
            )
            .OrderByDescending(n => n.PublishedAt ?? n.ApprovedAt ?? n.CreatedAt ?? DateTimeOffset.MinValue)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

            if (l10nMatches.Count > 0)
                return l10nMatches;
            // else fall through to base columns
        }

        return await _context.KnowledgeNodes
            .Where(n => n.IsCurrent && n.Status == "Current" &&
                        (EF.Functions.Like(n.Name ?? string.Empty, $"%{q}%") ||
                         EF.Functions.Like(n.Description ?? string.Empty, $"%{q}%")))
            .OrderByDescending(n => n.PublishedAt ?? n.ApprovedAt ?? n.CreatedAt ?? DateTimeOffset.MinValue)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }
}
