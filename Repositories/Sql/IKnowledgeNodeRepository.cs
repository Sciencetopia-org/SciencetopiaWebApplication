using Sciencetopia.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

public interface IKnowledgeNodeRepository
{
    Task<IEnumerable<Guid>> GetAllNodeIdsAsync();
    Task<(string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)?> GetNodeDetailsByIdAsync(Guid id, string language = "zh");
    Task<Dictionary<Guid, (string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)>> GetNodesDetailsAsync(IEnumerable<Guid> ids, string language = "zh");
    Task<Dictionary<Guid, string>> GetNodesNamesAsync(IEnumerable<Guid> ids, string language = "zh");
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
        var node = await _context.KnowledgeNodes
            .Where(n => n.IsCurrent && n.Status == "Current" &&
                        ((n.Id.HasValue && n.Id.Value == id) || n.StableId == id))
            .OrderByDescending(n => n.VersionNumber)
            .FirstOrDefaultAsync();
        if (node == null) return null;

        // L10n-aware override if enabled
        var opts = _context.GetService<Microsoft.Extensions.Options.IOptions<Sciencetopia.Services.L10n.L10nOptions>>();
        if (opts.Value.Enabled)
        {
            var l10n = _context.GetService<Sciencetopia.Services.L10n.IL10nService>();
            var title = await l10n.GetLocalizedAsync(id, "name", language);
            var desc = await l10n.GetLocalizedAsync(id, "description", language);
            return (
                (title ?? node.Name) ?? string.Empty,
                (desc ?? node.Description) ?? string.Empty,
                node.CreatedAt ?? default,
                node.PublishedAt ?? node.ApprovedAt ?? node.CreatedAt ?? default
            );
        }

        return (
            node.Name ?? string.Empty,
            node.Description ?? string.Empty,
            node.CreatedAt ?? default,
            node.PublishedAt ?? node.ApprovedAt ?? node.CreatedAt ?? default
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
