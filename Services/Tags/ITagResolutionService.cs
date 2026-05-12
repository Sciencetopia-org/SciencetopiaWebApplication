using Sciencetopia.Data;
using Microsoft.EntityFrameworkCore;

public interface ITagResolutionService
{
    Task<(IReadOnlyList<Guid> ResolvedTagIds, IReadOnlyList<Guid> CreatedTagIds)> ResolveOrCreateAsync(IEnumerable<string> names, string submittedBy);
    Task EnsureUncategorizedAsync(Guid tagStableId);
}

public class TagResolutionService : ITagResolutionService
{
    private readonly ApplicationDbContext _db;
    private readonly Sciencetopia.Services.L10n.IL10nService _l10n;
    private readonly Sciencetopia.Middleware.ILanguageContext _langCtx;
    private static string NormalizeKey(string value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    public TagResolutionService(ApplicationDbContext db,
                                Sciencetopia.Services.L10n.IL10nService l10n,
                                Sciencetopia.Middleware.ILanguageContext langCtx)
    {
        _db = db;
        _l10n = l10n;
        _langCtx = langCtx;
    }

    public async Task<(IReadOnlyList<Guid> ResolvedTagIds, IReadOnlyList<Guid> CreatedTagIds)> ResolveOrCreateAsync(IEnumerable<string> names, string submittedBy)
    {
        var list = (names ?? Enumerable.Empty<string>())
            .Select(n => (Original: n, Clean: (n ?? string.Empty).Trim()))
            .Where(t => !string.IsNullOrWhiteSpace(t.Clean))
            .ToList();

        if (list.Count == 0) return (Array.Empty<Guid>(), Array.Empty<Guid>());

        // Match by base name and L10n name across all languages, case-insensitive
        var lowerNames = list.Select(t => NormalizeKey(t.Clean)).Distinct().ToList();
        var lowerNameSet = lowerNames.ToHashSet();
        var lang = _langCtx.EffectiveLang;

        var baseCandidates = await _db.Tags
            .Where(t => t.Id.HasValue && t.IsCurrent && t.Status == "Current" && t.Name != null)
            .Select(t => new { t.Id, t.CreatedAt, Name = t.Name })
            .ToListAsync();

        var l10nCandidates = await (
            from t in _db.Tags
            where t.Id.HasValue && t.IsCurrent && t.Status == "Current"
            join tls in _db.TagL10nSets on t.Id!.Value equals tls.TagId
            join si in _db.L10nSetItems on tls.L10nSetId equals si.L10nSetId
            join i in _db.L10nItems on si.L10nItemId equals i.L10nItemId
            where i.FieldKey == "name"
            select new { t.Id, t.CreatedAt, i.LangCode, Name = i.Content ?? i.Text }
        ).ToListAsync();

        var candidates = new List<(string NameLower, Guid Id, DateTimeOffset? CreatedAt, int Rank)>();

        foreach (var row in baseCandidates)
        {
            if (!row.Id.HasValue || string.IsNullOrWhiteSpace(row.Name)) continue;
            var key = NormalizeKey(row.Name);
            if (lowerNameSet.Contains(key))
            {
                candidates.Add((key, row.Id.Value, row.CreatedAt, 0));
            }
        }

        foreach (var row in l10nCandidates)
        {
            if (!row.Id.HasValue || string.IsNullOrWhiteSpace(row.Name)) continue;
            var key = NormalizeKey(row.Name);
            if (!lowerNameSet.Contains(key)) continue;
            var rank = 3;
            if (!string.IsNullOrWhiteSpace(lang) && string.Equals(row.LangCode, lang, StringComparison.OrdinalIgnoreCase))
            {
                rank = 1;
            }
            else if (row.LangCode == null)
            {
                rank = 2;
            }
            candidates.Add((key, row.Id.Value, row.CreatedAt, rank));
        }

        var byLower = candidates
            .GroupBy(c => c.NameLower)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderBy(c => c.Rank)
                    .ThenBy(c => c.CreatedAt ?? DateTimeOffset.MinValue)
                    .ThenBy(c => c.Id)
                    .Select(c => c.Id)
                    .First()
            );

        var resolved = new List<Guid>();
        var created = new List<Guid>();

        foreach (var item in list)
        {
            var key = NormalizeKey(item.Clean);
            if (byLower.TryGetValue(key, out var id))
            {
                resolved.Add(id);
            }
            else
            {
                // Create new Tag
                var now = DateTimeOffset.UtcNow;
                var entity = new Tags
                {
                    Id = Guid.NewGuid(),
                    StableId = Guid.NewGuid(),
                    VersionNumber = 1,
                    Status = "Current",
                    IsCurrent = true,
                    Name = item.Clean,
                    Description = null,
                    CreatedAt = now,
                    CreatedBy = submittedBy,
                    PublishedAt = now,
                    ApprovedAt = now,
                    ApprovedBy = submittedBy
                };
                _db.Tags.Add(entity);
                await _db.SaveChangesAsync();

                if (entity.Id.HasValue)
                {
                    // Create L10n name for this tag in effective language
                    var useLang = lang ?? "zh";
                    await _l10n.UpsertForTagAsync(entity.Id.Value, "name", useLang, item.Clean, false, true, 0);
                    await EnsureUncategorizedAsync(entity.StableId);
                    created.Add(entity.Id.Value);
                    resolved.Add(entity.Id.Value);
                }
            }
        }

        return (resolved.Distinct().ToList(), created);
    }

    public async Task EnsureUncategorizedAsync(Guid tagStableId)
    {
        // Ensure TypesOfTags has an "Uncategorized" row
        var type = await _db.TypesOfTags.FirstOrDefaultAsync(t => t.Type == "Uncategorized");
        if (type == null)
        {
            type = new TypesOfTags { Type = "Uncategorized" };
            _db.TypesOfTags.Add(type);
            await _db.SaveChangesAsync();
        }

        // Ensure TagTypes relation exists
        var exists = await _db.TagTypes.AnyAsync(tt => tt.TagStableId == tagStableId && tt.TypeId == type.Id);
        if (!exists)
        {
            _db.TagTypes.Add(new TagTypes { TagStableId = tagStableId, TypeId = type.Id });
            await _db.SaveChangesAsync();
        }
    }
}
