using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Sciencetopia.Data;
using Sciencetopia.Models.L10n;
using Sciencetopia.Models.Ontology;
using Sciencetopia.Services.L10n;
using Sciencetopia.Services.Ontology;
using Xunit;

namespace SciencetopiaWebApplication.Tests;

/// <summary>
/// Phase 2: behavior tests for the flag-gated EntityL10nSets read shim
/// (IL10nService.GetLocalizedForEntityAsync). Read-only; runs on EF InMemory.
///
/// Behavior contract:
///  - L10nV2Enabled = false  -> EntityL10nSets is NEVER consulted; legacy node/tag paths unchanged.
///  - L10nV2Enabled = true   -> EntityL10nSets tried first for supported (entityType, purpose);
///                              on miss, legacy fallback for "KnowledgeNode"/"Tag", else null.
///  - Locale fallback        -> exact-lang Primary -> LangCode null Primary -> null (same as legacy).
/// </summary>
public class OntologyV2L10nShimTests
{
    private static ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static IL10nService Service(ApplicationDbContext db, bool v2Enabled) =>
        new L10nService(
            db,
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new L10nOptions { Enabled = true }),
            Options.Create(new OntologyOptions { L10nV2Enabled = v2Enabled }));

    private static Guid SeedSet(ApplicationDbContext db, params (string field, string? lang, string text)[] items)
    {
        var setId = Guid.NewGuid();
        db.L10nSets.Add(new L10nSet { L10nSetId = setId, Scope = "v2test" });
        foreach (var (field, lang, text) in items)
        {
            var itemId = Guid.NewGuid();
            db.L10nItems.Add(new L10nItem { L10nItemId = itemId, FieldKey = field, LangCode = lang, Kind = L10nItemKind.Primary, Text = text });
            db.L10nSetItems.Add(new L10nSetItem { L10nSetId = setId, L10nItemId = itemId });
        }
        db.SaveChanges();
        return setId;
    }

    // entityStableId = the target entity's SEMANTIC StableId (never a row/version Id).
    private static void BindEntity(ApplicationDbContext db, string type, Guid entityStableId, string purpose, Guid setId)
    {
        db.EntityL10nSets.Add(new EntityL10nSet
        {
            Id = Guid.NewGuid(), EntityType = type, EntityStableId = entityStableId, Purpose = purpose,
            L10nSetId = setId, CreatedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
    }

    private static Guid SeedNode(ApplicationDbContext db, Guid setId)
    {
        var id = Guid.NewGuid();
        db.KnowledgeNodes.Add(new KnowledgeNode { Id = id, StableId = Guid.NewGuid(), Status = "Current", IsCurrent = true, DefaultL10nSetId = setId });
        db.SaveChanges();
        return id;
    }

    private static Guid SeedTag(ApplicationDbContext db, Guid setId)
    {
        var id = Guid.NewGuid();
        db.Tags.Add(new Tags { Id = id, StableId = Guid.NewGuid(), Status = "Current", IsCurrent = true, DefaultL10nSetId = setId });
        db.SaveChanges();
        return id;
    }

    // 1. Flag OFF: EntityL10nSets is not consulted; legacy behavior is unchanged.
    [Fact]
    public async Task FlagOff_DoesNotConsultV2_AndLegacyPathUnchanged()
    {
        using var db = NewDb();
        var svc = Service(db, v2Enabled: false);

        // V2 binding exists for a supported type, but the flag is OFF -> must be ignored.
        var resourceId = Guid.NewGuid();
        BindEntity(db, EntityL10nTypes.Resource, resourceId, EntityL10nPurposes.Label,
            SeedSet(db, ("name", "en", "V2 Resource")));
        Assert.Null(await svc.GetLocalizedForEntityAsync(EntityL10nTypes.Resource, resourceId, EntityL10nPurposes.Label, "en"));

        // Legacy node path is unchanged and still works (directly and via the generalized route).
        var nodeId = SeedNode(db, SeedSet(db, ("name", "en", "Legacy Node")));
        Assert.Equal("Legacy Node", await svc.GetLocalizedAsync(nodeId, "name", "en"));
        Assert.Equal("Legacy Node", await svc.GetLocalizedForEntityAsync("KnowledgeNode", nodeId, EntityL10nPurposes.Label, "en"));
    }

    // 2. Flag ON + V2 hit: returns the localized value from EntityL10nSets.
    [Fact]
    public async Task FlagOn_V2Hit_ReturnsEntityL10nSetsValue()
    {
        using var db = NewDb();
        var svc = Service(db, v2Enabled: true);

        var resourceId = Guid.NewGuid();
        BindEntity(db, EntityL10nTypes.Resource, resourceId, EntityL10nPurposes.Label,
            SeedSet(db, ("name", "en", "V2 Resource Name")));

        Assert.Equal("V2 Resource Name",
            await svc.GetLocalizedForEntityAsync(EntityL10nTypes.Resource, resourceId, EntityL10nPurposes.Label, "en"));
    }

    // 3. Flag ON + no V2 hit: falls back to the legacy node/tag read path where applicable.
    [Fact]
    public async Task FlagOn_NoV2Hit_FallsBackToLegacyNodeAndTag()
    {
        using var db = NewDb();
        var svc = Service(db, v2Enabled: true);

        var nodeId = SeedNode(db, SeedSet(db, ("name", "zh", "节点名")));
        var tagId = SeedTag(db, SeedSet(db, ("name", "zh", "标签名")));

        // No EntityL10nSets rows for these -> legacy fallback.
        Assert.Equal("节点名", await svc.GetLocalizedForEntityAsync("KnowledgeNode", nodeId, EntityL10nPurposes.Label, "zh"));
        Assert.Equal("标签名", await svc.GetLocalizedForEntityAsync("Tag", tagId, EntityL10nPurposes.Label, "zh"));
    }

    // 4. Locale fallback follows the confirmed legacy behavior: exact lang -> neutral -> null.
    [Fact]
    public async Task FlagOn_LocaleFallback_MatchesLegacyBehavior()
    {
        using var db = NewDb();
        var svc = Service(db, v2Enabled: true);

        var resourceId = Guid.NewGuid();
        BindEntity(db, EntityL10nTypes.Resource, resourceId, EntityL10nPurposes.Label,
            SeedSet(db, ("name", null, "Neutral"), ("name", "en", "English")));

        Assert.Equal("English", await svc.GetLocalizedForEntityAsync(EntityL10nTypes.Resource, resourceId, EntityL10nPurposes.Label, "en")); // exact
        Assert.Equal("Neutral", await svc.GetLocalizedForEntityAsync(EntityL10nTypes.Resource, resourceId, EntityL10nPurposes.Label, "fr")); // -> neutral
        Assert.Equal("Neutral", await svc.GetLocalizedForEntityAsync(EntityL10nTypes.Resource, resourceId, EntityL10nPurposes.Label, null)); // -> neutral
    }

    // 5. Unsupported entity type / purpose fails safe (returns null, no exception).
    [Fact]
    public async Task UnsupportedEntityOrPurpose_FailsSafe()
    {
        using var db = NewDb();
        var svc = Service(db, v2Enabled: true);

        // Unsupported purpose -> null.
        var rid = Guid.NewGuid();
        BindEntity(db, EntityL10nTypes.Resource, rid, EntityL10nPurposes.Label, SeedSet(db, ("name", "en", "X")));
        Assert.Null(await svc.GetLocalizedForEntityAsync(EntityL10nTypes.Resource, rid, "bogus_purpose", "en"));

        // Unsupported entity type with no legacy fallback -> null (no throw).
        Assert.Null(await svc.GetLocalizedForEntityAsync("Galaxy", Guid.NewGuid(), EntityL10nPurposes.Label, "en"));
    }

    // 6. StableId semantics: the binding key is EntityStableId (the concept's semantic StableId).
    //    Multiple locales attach as L10n data to ONE semantic entity; adding a translation does
    //    not mint a second semantic entity.
    [Fact]
    public async Task LocalizedVariants_AttachToSingleStableIdEntity_NoDuplicateSemanticEntity()
    {
        using var db = NewDb();
        var svc = Service(db, v2Enabled: true);

        var conceptStableId = Guid.NewGuid();
        // One set, two locales, ONE EntityL10nSets binding keyed by the concept's StableId.
        BindEntity(db, EntityL10nTypes.Concept, conceptStableId, EntityL10nPurposes.Label,
            SeedSet(db, ("name", "en", "Statistical Mechanics"), ("name", "zh", "统计力学")));

        Assert.Equal("Statistical Mechanics",
            await svc.GetLocalizedForEntityAsync(EntityL10nTypes.Concept, conceptStableId, EntityL10nPurposes.Label, "en"));
        Assert.Equal("统计力学",
            await svc.GetLocalizedForEntityAsync(EntityL10nTypes.Concept, conceptStableId, EntityL10nPurposes.Label, "zh"));

        // The binding is keyed by EntityStableId; both locales resolve through that SINGLE semantic
        // identity — exactly one binding row exists, regardless of how many localized variants.
        Assert.Equal(1, db.EntityL10nSets.Count(e => e.EntityType == EntityL10nTypes.Concept && e.EntityStableId == conceptStableId));
        Assert.Equal(2, db.L10nItems.Count()); // two localized texts, one semantic entity
    }
}
