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

        // Match by L10n name in effective language (fallback to null lang), case-insensitive
        var lowerNames = list.Select(t => t.Clean.ToLower()).Distinct().ToList();
        var lang = _langCtx.EffectiveLang;

        var existing = await (
            from t in _db.Tags
            where t.Id.HasValue && t.IsCurrent && t.Status == "Current"
            join tls in _db.TagL10nSets on t.Id!.Value equals tls.TagId
            join si in _db.L10nSetItems on tls.L10nSetId equals si.L10nSetId
            join i in _db.L10nItems on si.L10nItemId equals i.L10nItemId
            where i.FieldKey == "name" && (i.LangCode == lang || i.LangCode == null)
            select new { t.Id, Name = i.Content ?? i.Text }
        ).ToListAsync();

        var byLower = existing
            .Where(x => x.Id.HasValue && !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => (x.Name ?? string.Empty).ToLower())
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id!.Value).First());

        var resolved = new List<Guid>();
        var created = new List<Guid>();

        foreach (var item in list)
        {
            var key = item.Clean.ToLower();
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
