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

    public KnowledgeNodeRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Guid>> GetAllNodeIdsAsync()
    {
        return await _context.KnowledgeNodes
                             .Where(node => node.Id != null)
                             .Select(node => node.Id.Value)
                             .ToListAsync();
    }

    public async Task<(string Name, string Description, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate)?> GetNodeDetailsByIdAsync(Guid id, string language = "zh")
    {
        var node = await _context.KnowledgeNodes.FirstOrDefaultAsync(n => n.Id == id);
        if (node == null) return null;

        // L10n-aware override if enabled
        var opts = _context.GetService<Microsoft.Extensions.Options.IOptions<Sciencetopia.Services.L10n.L10nOptions>>();
        if (opts.Value.Enabled)
        {
            var l10n = _context.GetService<Sciencetopia.Services.L10n.IL10nService>();
            var title = await l10n.GetLocalizedAsync(id, "title", language);
            var desc = await l10n.GetLocalizedAsync(id, "description", language);
            return (
                (title ?? node.Name) ?? string.Empty,
                (desc ?? node.Description) ?? string.Empty,
                node.CreatedDate ?? default,
                node.UpdatedDate ?? default
            );
        }

        return (
            node.Name ?? string.Empty,
            node.Description ?? string.Empty,
            node.CreatedDate ?? default,
            node.UpdatedDate ?? default
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
        var opts = _context.GetService<Microsoft.Extensions.Options.IOptions<Sciencetopia.Services.L10n.L10nOptions>>();
        var l10n = _context.GetService<Sciencetopia.Services.L10n.IL10nService>();
        if (opts.Value.Enabled)
        {
            var dict = new Dictionary<Guid, string>();
            foreach (var id in ids.Distinct())
            {
                var title = await l10n.GetLocalizedAsync(id, "title", language);
                dict[id] = string.IsNullOrWhiteSpace(title) ? id.ToString() : title!;
            }
            return dict;
        }
        // fallback to base field only
        var idSet = ids.ToHashSet();
        var rows = await _context.KnowledgeNodes
                          .Where(n => n.Id.HasValue && idSet.Contains(n.Id.Value))
                          .Select(n => new { Id = n.Id!.Value, Name = n.Name })
                          .ToListAsync();
        return rows.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First().Name ?? string.Empty);
    }

    public async Task<List<KnowledgeNode>> SearchKnowledgeNodesAsync(string query, int skip, int take)
    {
        return await _context.KnowledgeNodes
            .Where(n => EF.Functions.Like(n.Name, $"%{query}%") ||
                         EF.Functions.Like(n.Description, $"%{query}%"))
            .OrderByDescending(n => n.UpdatedDate)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }
}
