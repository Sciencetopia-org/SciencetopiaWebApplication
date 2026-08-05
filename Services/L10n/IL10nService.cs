using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Sciencetopia.Data;
using Sciencetopia.Models.L10n;
using Sciencetopia.Models.Ontology;
using Sciencetopia.Services.Ontology;

namespace Sciencetopia.Services.L10n
{
    public class L10nOptions
    {
        public bool Enabled { get; set; } = false;
    }

    public record L10nItemDto(Guid L10nItemId, string FieldKey, string? LangCode, L10nItemKind Kind, string? Text, string? Content, int SortOrder);

    public interface IL10nService
    {
        Task<string?> GetLocalizedAsync(Guid nodeId, string fieldKey, string? lang);
        Task<Dictionary<Guid, string>> GetLocalizedManyAsync(IEnumerable<Guid> nodeIds, string fieldKey, string? lang);
        Task<IReadOnlyList<L10nItemDto>> ListAsync(Guid nodeId, string fieldKey);
        Task<Guid> UpsertAsync(Guid nodeId, string fieldKey, string lang,
                         string value, bool isLongText = false,
                         bool primary = true, int? sortOrder = null);
        Task RemoveAsync(Guid nodeId, Guid l10nItemId);

        // Tag versions
        Task<string?> GetLocalizedForTagAsync(Guid tagId, string fieldKey, string? lang);
        Task<Dictionary<Guid, string>> GetLocalizedForTagManyAsync(IEnumerable<Guid> tagIds, string fieldKey, string? lang);
        Task<IReadOnlyList<L10nItemDto>> ListForTagAsync(Guid tagId, string fieldKey);
        Task<Guid> UpsertForTagAsync(Guid tagId, string fieldKey, string lang,
                         string value, bool isLongText = false,
                         bool primary = true, int? sortOrder = null);
        Task RemoveForTagAsync(Guid tagId, Guid l10nItemId);

        // --- Ontology V2 generalized read shim (Phase 2) ---
        // Resolves localized text for any entity via the generalized EntityL10nSets binding.
        // Gated by OntologyOptions.L10nV2Enabled: when the flag is ON, EntityL10nSets is tried
        // first for supported (entityType, purpose); when OFF, EntityL10nSets is never consulted.
        // In both cases, a legacy fallback to the existing node/tag read path is used where
        // applicable (entityType "KnowledgeNode" or "Tag"). Read-only; no writes.
        Task<string?> GetLocalizedForEntityAsync(string entityType, Guid entityStableId, string purpose, string? lang);
    }

        public class L10nService : IL10nService
        {
        private readonly ApplicationDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly IOptions<L10nOptions> _opts;
        private readonly IOptions<OntologyOptions> _ontology;

        public L10nService(ApplicationDbContext db, IMemoryCache cache, IOptions<L10nOptions> opts,
                           IOptions<OntologyOptions> ontology)
        {
            _db = db; _cache = cache; _opts = opts; _ontology = ontology;
        }

        // Entity types whose localized text can be stored in EntityL10nSets (Phase 1 set).
        private static readonly HashSet<string> SupportedV2EntityTypes = new(StringComparer.Ordinal)
        {
            EntityL10nTypes.Concept, EntityL10nTypes.ConceptPage, EntityL10nTypes.ConceptScheme,
            EntityL10nTypes.TagFacet, EntityL10nTypes.TagValue, EntityL10nTypes.Resource,
            EntityL10nTypes.StudyPlan, EntityL10nTypes.Lesson, EntityL10nTypes.StudyGroup
        };

        // Legacy entity types that have an existing (pre-V2) localized read path to fall back to.
        // NOT V2-storable; recognized only as fallback routes (see GetLocalizedForEntityAsync).
        private const string LegacyKnowledgeNode = "KnowledgeNode";
        private const string LegacyTag = "Tag";

        // Maps an EntityL10nPurpose to the L10nItem.FieldKey used in storage. Returns null for an
        // unsupported purpose (caller fails safe with null). Label/Description map to the existing
        // "name"/"description" field keys; other purposes use their lowercase purpose string.
        private static string? MapPurposeToFieldKey(string purpose) => purpose switch
        {
            EntityL10nPurposes.Label => "name",
            EntityL10nPurposes.Alias => "name",
            EntityL10nPurposes.Description => "description",
            EntityL10nPurposes.Title => "title",
            EntityL10nPurposes.Summary => "summary",
            EntityL10nPurposes.Overview => "overview",
            EntityL10nPurposes.Instruction => "instruction",
            EntityL10nPurposes.UiDisplay => "ui_display",
            EntityL10nPurposes.Legacy => "legacy",
            _ => null
        };

        // Resolves a localized value from a single L10n set using the SAME locale fallback as the
        // legacy read path: exact-lang Primary (by SortOrder) -> LangCode null Primary -> null.
        private async Task<string?> ResolveFromSetAsync(Guid setId, string fieldKey, string? lang)
        {
            var q = from si in _db.L10nSetItems.AsNoTracking()
                    join i in _db.L10nItems on si.L10nItemId equals i.L10nItemId
                    where si.L10nSetId == setId && i.FieldKey == fieldKey
                    select i;

            L10nItem? item = null;
            if (!string.IsNullOrWhiteSpace(lang))
                item = await q.Where(i => i.LangCode == lang && i.Kind == L10nItemKind.Primary)
                              .OrderBy(i => i.SortOrder).FirstOrDefaultAsync();
            item ??= await q.Where(i => i.LangCode == null && i.Kind == L10nItemKind.Primary)
                            .OrderBy(i => i.SortOrder).FirstOrDefaultAsync();

            return item == null ? null : (item.Content ?? item.Text);
        }

        // entityStableId = the SEMANTIC StableId of the target entity (NOT a row/version Id).
        // For legacy node/tag fallback this is matched against StableId (the legacy path also
        // accepts a version Id, but callers should pass the StableId).
        public async Task<string?> GetLocalizedForEntityAsync(string entityType, Guid entityStableId, string purpose, string? lang)
        {
            var fieldKey = MapPurposeToFieldKey(purpose);
            if (fieldKey == null) return null; // unsupported purpose -> fail safe (no throw)

            // V2 path: only when the flag is ON and (entityType, purpose) are supported for V2 storage.
            if (_ontology.Value.L10nV2Enabled && SupportedV2EntityTypes.Contains(entityType))
            {
                var setId = await _db.EntityL10nSets.AsNoTracking()
                    .Where(e => e.EntityType == entityType && e.EntityStableId == entityStableId && e.Purpose == purpose)
                    .OrderBy(e => e.CreatedAt)
                    .Select(e => (Guid?)e.L10nSetId)
                    .FirstOrDefaultAsync();

                if (setId.HasValue)
                {
                    var v2 = await ResolveFromSetAsync(setId.Value, fieldKey, lang);
                    if (v2 != null) return v2; // V2 hit
                }
                // V2 miss -> fall through to legacy fallback below.
            }

            // Legacy fallback where applicable. Reuses the existing (unchanged) read paths so locale
            // fallback and caching are identical to legacy behavior. Other entity types have no
            // pre-V2 localized store, so they return null.
            return entityType switch
            {
                LegacyKnowledgeNode => await GetLocalizedAsync(entityStableId, fieldKey, lang),
                LegacyTag => await GetLocalizedForTagAsync(entityStableId, fieldKey, lang),
                _ => null
            };
        }

        private static string CacheKey(Guid nodeId, string fieldKey, string? lang)
            => $"l10n:node:{nodeId}:field:{fieldKey}:lang:{lang ?? "_"}";

        private async Task<Guid> EnsurePrimarySetAsync(Guid entityId, string scope)
        {
            if (scope == "knowledge_node")
            {
                var node = await _db.KnowledgeNodes.FirstOrDefaultAsync(x => x.Id == entityId);
                if (node == null) throw new InvalidOperationException("Node not found");
                if (node.DefaultL10nSetId.HasValue) return node.DefaultL10nSetId.Value;
            }
            else if (scope == "tag")
            {
                var tag = await _db.Tags.FirstOrDefaultAsync(x => x.Id == entityId);
                if (tag == null) throw new InvalidOperationException("Tag not found");
                if (tag.DefaultL10nSetId.HasValue) return tag.DefaultL10nSetId.Value;
            }

            var set = new L10nSet { Scope = scope };
            _db.L10nSets.Add(set);
            await _db.SaveChangesAsync();
            if (scope == "knowledge_node")
            {
                var node = await _db.KnowledgeNodes.FirstAsync(x => x.Id == entityId);
                node.DefaultL10nSetId = set.L10nSetId;
                _db.NodeL10nSets.Add(new NodeL10nSet { NodeId = entityId, L10nSetId = set.L10nSetId, Relation = 0 });
            }
            else if (scope == "tag")
            {
                var tag = await _db.Tags.FirstAsync(x => x.Id == entityId);
                tag.DefaultL10nSetId = set.L10nSetId;
                _db.TagL10nSets.Add(new TagL10nSet { TagId = entityId, L10nSetId = set.L10nSetId, Relation = 0 });
            }
            await _db.SaveChangesAsync();
            return set.L10nSetId;
        }

        public async Task<string?> GetLocalizedAsync(Guid nodeId, string fieldKey, string? lang)
        {
            var key = CacheKey(nodeId, fieldKey, lang);
            if (_cache.TryGetValue(key, out string? cached)) return cached;

            // Support both version-id and stable-id inputs
            var setId = await _db.KnowledgeNodes
                .AsNoTracking()
                .Where(n => (n.Id.HasValue && n.Id.Value == nodeId) || n.StableId == nodeId)
                .Select(n => n.DefaultL10nSetId)
                .FirstOrDefaultAsync();

            if (!setId.HasValue) return null;

            // prefer exact lang primary
            var q = from si in _db.L10nSetItems
                    join i in _db.L10nItems on si.L10nItemId equals i.L10nItemId
                    where si.L10nSetId == setId.Value && i.FieldKey == fieldKey
                    select i;

            L10nItem? item = null;
            if (!string.IsNullOrWhiteSpace(lang))
            {
                item = await q.Where(i => i.LangCode == lang && i.Kind == L10nItemKind.Primary)
                              .OrderBy(i => i.SortOrder).FirstOrDefaultAsync();
            }

            // fallback to LangCode null
            item ??= await q.Where(i => i.LangCode == null && i.Kind == L10nItemKind.Primary)
                            .OrderBy(i => i.SortOrder).FirstOrDefaultAsync();

            string? value = item == null ? null : (item.Content ?? item.Text);
            _cache.Set(key, value, TimeSpan.FromMinutes(5));
            return value;
        }

        public async Task<Dictionary<Guid, string>> GetLocalizedManyAsync(IEnumerable<Guid> nodeIds, string fieldKey, string? lang)
        {
            var idSet = nodeIds?.ToHashSet() ?? new HashSet<Guid>();
            var result = new Dictionary<Guid, string>();
            if (idSet.Count == 0) return result;

            // Load nodes matching either version-id or stable-id from the inputs
            var nodes = await _db.KnowledgeNodes
                .AsNoTracking()
                .Where(n => n.Id.HasValue)
                .Where(n => idSet.Contains(n.Id!.Value) || idSet.Contains(n.StableId))
                .Select(n => new { VersionId = n.Id!.Value, n.StableId, n.DefaultL10nSetId })
                .ToListAsync();

            // Build mapping from each requested id (version or stable) to the L10n set id
            var mapRequestedIdToSet = new Dictionary<Guid, Guid>();
            foreach (var n in nodes)
            {
                if (!n.DefaultL10nSetId.HasValue) continue;
                var setId = n.DefaultL10nSetId.Value;
                if (idSet.Contains(n.VersionId)) mapRequestedIdToSet[n.VersionId] = setId;
                if (idSet.Contains(n.StableId)) mapRequestedIdToSet[n.StableId] = setId;
            }

            if (mapRequestedIdToSet.Count == 0) return result;

            var setIds = mapRequestedIdToSet.Values.Distinct().ToList();
            var items = await (from si in _db.L10nSetItems
                               .AsNoTracking()
                               join i in _db.L10nItems on si.L10nItemId equals i.L10nItemId
                               where setIds.Contains(si.L10nSetId)
                                     && i.FieldKey == fieldKey
                                     && i.Kind == L10nItemKind.Primary
                                     && (i.LangCode == lang || i.LangCode == null)
                               select new { si.L10nSetId, i.LangCode, Text = i.Content ?? i.Text, i.SortOrder })
                               .ToListAsync();

            var bestBySet = items
                .GroupBy(x => x.L10nSetId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(x => x.LangCode == lang ? 0 : 1).ThenBy(x => x.SortOrder).First().Text ?? string.Empty
                );

            // Return results keyed by the requested ids to align with callers
            foreach (var (requestedId, setId) in mapRequestedIdToSet)
            {
                if (bestBySet.TryGetValue(setId, out var text) && !string.IsNullOrWhiteSpace(text))
                    result[requestedId] = text;
            }

            return result;
        }

        public async Task<IReadOnlyList<L10nItemDto>> ListAsync(Guid nodeId, string fieldKey)
        {
            var setId = await _db.KnowledgeNodes.Where(n => n.Id == nodeId).Select(n => n.DefaultL10nSetId).FirstOrDefaultAsync();
            if (!setId.HasValue) return Array.Empty<L10nItemDto>();
            var rows = await (from si in _db.L10nSetItems
                              join i in _db.L10nItems on si.L10nItemId equals i.L10nItemId
                              where si.L10nSetId == setId.Value && i.FieldKey == fieldKey
                              orderby i.SortOrder
                              select new L10nItemDto(i.L10nItemId, i.FieldKey, i.LangCode, i.Kind, i.Text, i.Content, i.SortOrder))
                              .ToListAsync();
            return rows;
        }

        public async Task<Guid> UpsertAsync(Guid nodeId, string fieldKey, string lang, string value, bool isLongText = false, bool primary = true, int? sortOrder = null)
        {
            var setId = await EnsurePrimarySetAsync(nodeId, "knowledge_node");
            var q = from si in _db.L10nSetItems
                    join i in _db.L10nItems on si.L10nItemId equals i.L10nItemId
                    where si.L10nSetId == setId && i.FieldKey == fieldKey && i.LangCode == lang
                    select i;
            var entity = await q.FirstOrDefaultAsync();
            if (entity == null)
            {
                entity = new L10nItem
                {
                    FieldKey = fieldKey,
                    LangCode = lang,
                    Kind = primary ? L10nItemKind.Primary : L10nItemKind.Alias,
                    SortOrder = sortOrder ?? 0,
                };
                if (isLongText) entity.Content = value; else entity.Text = value;
                _db.L10nItems.Add(entity);
                await _db.SaveChangesAsync();
                _db.L10nSetItems.Add(new L10nSetItem { L10nSetId = setId, L10nItemId = entity.L10nItemId });
            }
            else
            {
                if (isLongText) { entity.Content = value; } else { entity.Text = value; }
                if (primary) entity.Kind = L10nItemKind.Primary;
                if (sortOrder.HasValue) entity.SortOrder = sortOrder.Value;
            }
            await _db.SaveChangesAsync();
            // cache invalidation
            _cache.Remove(CacheKey(nodeId, fieldKey, lang));
            return entity.L10nItemId;
        }

        public async Task RemoveAsync(Guid nodeId, Guid l10nItemId)
        {
            var item = await _db.L10nItems.FirstOrDefaultAsync(x => x.L10nItemId == l10nItemId);
            if (item != null)
            {
                _db.L10nItems.Remove(item);
                await _db.SaveChangesAsync();
                // best-effort invalidate for both null and non-null lang
                _cache.Remove(CacheKey(nodeId, item.FieldKey, item.LangCode));
            }
        }

        // Tag variants
        public async Task<string?> GetLocalizedForTagAsync(Guid tagId, string fieldKey, string? lang)
        {
            var key = CacheKey(tagId, fieldKey, lang);
            if (_cache.TryGetValue(key, out string? cached)) return cached;

            var setId = await _db.Tags.AsNoTracking().Where(t => t.Id == tagId).Select(t => t.DefaultL10nSetId).FirstOrDefaultAsync();
            if (!setId.HasValue) return null;
            var q = from si in _db.L10nSetItems
                    join i in _db.L10nItems on si.L10nItemId equals i.L10nItemId
                    where si.L10nSetId == setId.Value && i.FieldKey == fieldKey
                    select i;
            L10nItem? item = null;
            if (!string.IsNullOrWhiteSpace(lang))
                item = await q.Where(i => i.LangCode == lang && i.Kind == L10nItemKind.Primary).OrderBy(i => i.SortOrder).FirstOrDefaultAsync();
            item ??= await q.Where(i => i.LangCode == null && i.Kind == L10nItemKind.Primary).OrderBy(i => i.SortOrder).FirstOrDefaultAsync();
            string? value = item == null ? null : (item.Content ?? item.Text);
            _cache.Set(key, value, TimeSpan.FromMinutes(5));
            return value;
        }

        public async Task<Dictionary<Guid, string>> GetLocalizedForTagManyAsync(IEnumerable<Guid> tagIds, string fieldKey, string? lang)
        {
            var idSet = tagIds?.ToHashSet() ?? new HashSet<Guid>();
            var result = new Dictionary<Guid, string>();
            if (idSet.Count == 0) return result;

            var sortedIds = idSet.OrderBy(x => x).ToList();
            var cacheKey = $"l10n:tagmany:{fieldKey}:{lang ?? "_"}:{string.Join(",", sortedIds)}";
            if (_cache.TryGetValue(cacheKey, out Dictionary<Guid, string>? cached) && cached != null)
            {
                return cached;
            }

            var tags = await _db.Tags
                .AsNoTracking()
                .Where(t => t.Id.HasValue && (idSet.Contains(t.Id.Value) || idSet.Contains(t.StableId)))
                .Select(t => new { VersionId = t.Id!.Value, t.StableId, t.DefaultL10nSetId })
                .ToListAsync();

            var mapTagToSet = new Dictionary<Guid, Guid>();
            foreach (var tag in tags.Where(x => x.DefaultL10nSetId.HasValue))
            {
                var setId = tag.DefaultL10nSetId!.Value;
                if (idSet.Contains(tag.VersionId))
                {
                    mapTagToSet[tag.VersionId] = setId;
                }

                if (idSet.Contains(tag.StableId))
                {
                    mapTagToSet[tag.StableId] = setId;
                }
            }

            if (mapTagToSet.Count == 0) return result;

            var setIds = mapTagToSet.Values.Distinct().ToList();
            var items = await (from si in _db.L10nSetItems
                               .AsNoTracking()
                               join i in _db.L10nItems on si.L10nItemId equals i.L10nItemId
                               where setIds.Contains(si.L10nSetId)
                                     && i.FieldKey == fieldKey
                                     && i.Kind == L10nItemKind.Primary
                                     && (i.LangCode == lang || i.LangCode == null)
                               select new { si.L10nSetId, i.LangCode, Text = i.Content ?? i.Text, i.SortOrder })
                               .ToListAsync();

            var bestBySet = items
                .GroupBy(x => x.L10nSetId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(x => x.LangCode == lang ? 0 : 1).ThenBy(x => x.SortOrder).First().Text ?? string.Empty
                );

            foreach (var (tagId, setId) in mapTagToSet)
            {
                if (bestBySet.TryGetValue(setId, out var text) && !string.IsNullOrWhiteSpace(text))
                    result[tagId] = text;
            }

            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(10));
            return result;
        }

        public async Task<IReadOnlyList<L10nItemDto>> ListForTagAsync(Guid tagId, string fieldKey)
        {
            var setId = await _db.Tags.Where(t => t.Id == tagId).Select(t => t.DefaultL10nSetId).FirstOrDefaultAsync();
            if (!setId.HasValue) return Array.Empty<L10nItemDto>();
            var rows = await (from si in _db.L10nSetItems
                              join i in _db.L10nItems on si.L10nItemId equals i.L10nItemId
                              where si.L10nSetId == setId.Value && i.FieldKey == fieldKey
                              orderby i.SortOrder
                              select new L10nItemDto(i.L10nItemId, i.FieldKey, i.LangCode, i.Kind, i.Text, i.Content, i.SortOrder))
                              .ToListAsync();
            return rows;
        }

        public async Task<Guid> UpsertForTagAsync(Guid tagId, string fieldKey, string lang, string value, bool isLongText = false, bool primary = true, int? sortOrder = null)
        {
            var setId = await EnsurePrimarySetAsync(tagId, "tag");
            var q = from si in _db.L10nSetItems
                    join i in _db.L10nItems on si.L10nItemId equals i.L10nItemId
                    where si.L10nSetId == setId && i.FieldKey == fieldKey && i.LangCode == lang
                    select i;
            var entity = await q.FirstOrDefaultAsync();
            if (entity == null)
            {
                entity = new L10nItem
                {
                    FieldKey = fieldKey,
                    LangCode = lang,
                    Kind = primary ? L10nItemKind.Primary : L10nItemKind.Alias,
                    SortOrder = sortOrder ?? 0,
                };
                if (isLongText) entity.Content = value; else entity.Text = value;
                _db.L10nItems.Add(entity);
                await _db.SaveChangesAsync();
                _db.L10nSetItems.Add(new L10nSetItem { L10nSetId = setId, L10nItemId = entity.L10nItemId });
            }
            else
            {
                if (isLongText) { entity.Content = value; } else { entity.Text = value; }
                if (primary) entity.Kind = L10nItemKind.Primary;
                if (sortOrder.HasValue) entity.SortOrder = sortOrder.Value;
            }
            await _db.SaveChangesAsync();
            _cache.Remove(CacheKey(tagId, fieldKey, lang));
            return entity.L10nItemId;
        }

        public async Task RemoveForTagAsync(Guid tagId, Guid l10nItemId)
        {
            var item = await _db.L10nItems.FirstOrDefaultAsync(x => x.L10nItemId == l10nItemId);
            if (item != null)
            {
                _db.L10nItems.Remove(item);
                await _db.SaveChangesAsync();
                _cache.Remove(CacheKey(tagId, item.FieldKey, item.LangCode));
            }
        }
    }
}
