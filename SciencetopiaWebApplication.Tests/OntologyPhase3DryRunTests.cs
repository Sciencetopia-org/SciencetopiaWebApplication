using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Models.Ontology;
using Sciencetopia.Services.Ontology.Phase3;
using Xunit;

namespace SciencetopiaWebApplication.Tests;

/// <summary>
/// Phase 3: deterministic dry-run classification tests. All pure (no DB/Neo4j); the one DB test
/// asserts the engine performs no writes. No legacy mutation, no Neo4j writes, no canonical rows.
/// </summary>
public class OntologyPhase3DryRunTests
{
    private static LegacyTagInput Tag(string? name, Guid? stableId = null, Guid? versionId = null) =>
        new(versionId ?? Guid.NewGuid(), stableId ?? Guid.NewGuid(), name, "Current", true, false);

    private static LegacyTagInput TagWithL10n(string? name, bool hasL10n, Guid? stableId = null, Guid? versionId = null) =>
        new(versionId ?? Guid.NewGuid(), stableId ?? Guid.NewGuid(), name, "Current", true, hasL10n);

    private static SqlTagIdentityInput SqlVersion(
        Guid versionId,
        Guid stableId,
        bool isCurrent = true,
        string? name = "Tag",
        string? status = "Current",
        bool hasL10n = false) =>
        new(versionId, stableId, name, status, isCurrent, hasL10n);

    private static Phase3Inputs Inputs(
        IEnumerable<LegacyTagInput>? sql = null,
        IEnumerable<Neo4jTagInput>? neo = null,
        IEnumerable<TagLevelInput>? levels = null,
        IEnumerable<ContainEdgeInput>? contains = null,
        IEnumerable<SqlTagIdentityInput>? sqlVersions = null) =>
        new((sql ?? Enumerable.Empty<LegacyTagInput>()).ToList(),
            (neo ?? Enumerable.Empty<Neo4jTagInput>()).ToList(),
            (levels ?? Enumerable.Empty<TagLevelInput>()).ToList(),
            (contains ?? Enumerable.Empty<ContainEdgeInput>()).ToList(),
            sqlVersions?.ToList());

    // 1. Malformed SQL Tag name parse.
    [Fact]
    public void MalformedName_ParsesCleanNameAndStableId()
    {
        var parsed = LegacyTagClassifier.ParseMalformedName("宇宙学 23a78f8d576080e090e9ecac150a51c5");
        Assert.NotNull(parsed);
        Assert.Equal("宇宙学", parsed!.Value.CleanName);
        Assert.Equal("23a78f8d576080e090e9ecac150a51c5", parsed.Value.Hex);

        var result = Phase3DryRunEngine.Run(Inputs(sql: new[] { Tag("宇宙学 23a78f8d576080e090e9ecac150a51c5") }));
        var c = Assert.Single(result.LegacyTagInventory);
        Assert.Equal(Phase3Buckets.DirtyLegacy, c.Bucket);
        Assert.Equal(Phase3Actions.DirtyLegacyParseCandidate, c.Action);
        Assert.Equal("宇宙学", c.CandidateName);
        Assert.Equal("23a78f8d576080e090e9ecac150a51c5", c.CandidateStableId);
        Assert.Equal(OntologyReviewStatuses.Pending, c.ReviewStatus);
    }

    // 2. Duplicate StableId -> quarantine / merge proposal, never auto-merge.
    [Fact]
    public void DuplicateStableId_QuarantinesAsMergeProposal_NoAutoMerge()
    {
        var dup = Guid.NewGuid();
        var result = Phase3DryRunEngine.Run(Inputs(sql: new[] { Tag("A", dup), Tag("B", dup) }));

        Assert.All(result.LegacyTagInventory, c => Assert.Equal(Phase3Actions.MergeProposal, c.Action));
        Assert.Contains(result.Quarantine, q => q.Kind == "duplicate_stableid");
        // No concept candidate is minted for ambiguous duplicates.
        Assert.Empty(result.ConceptCandidates);
    }

    // 3. Same normalized name, different StableId -> alias proposal, not merge.
    [Fact]
    public void SameName_DifferentStableId_IsAliasProposal_NotMerge()
    {
        var result = Phase3DryRunEngine.Run(Inputs(sql: new[] { Tag("Group"), Tag("group") }));

        Assert.All(result.LegacyTagInventory, c => Assert.Equal(Phase3Actions.NameCollisionAliasCandidate, c.Action));
        Assert.Contains(result.Quarantine, q => q.Kind == "name_collision");
        Assert.Empty(result.ConceptCandidates); // ambiguous -> no concept candidate
    }

    // 4. Missing StableId -> quarantine.
    [Fact]
    public void MissingStableId_IsQuarantined()
    {
        var result = Phase3DryRunEngine.Run(Inputs(sql: new[] { Tag("orphan", Guid.Empty) }));
        var c = Assert.Single(result.LegacyTagInventory);
        Assert.Equal(Phase3Actions.Quarantine, c.Action);
        Assert.Contains(result.Quarantine, q => q.Kind == "missing_stableid");
    }

    // 5. Neo4j tag without stableId but with a StudyGroup id path -> NOT dirty (studygroup_keyed).
    [Fact]
    public void Neo4jTag_NoStableId_WithStudyGroupId_IsStudyGroupKeyed_NotDirty()
    {
        var rowId = Guid.NewGuid();
        var stableId = Guid.NewGuid();
        var neo = new[] { new Neo4jTagInput(StableId: null, Id: rowId.ToString(),
            LinkedToKnowledgeNode: false, LinkedToStudyGroup: true, InContainHierarchy: false) };
        var result = Phase3DryRunEngine.Run(Inputs(
            neo: neo,
            sqlVersions: new[] { SqlVersion(rowId, stableId) }));

        var c = Assert.Single(result.Neo4jTagInventory);
        Assert.Equal(Phase3Actions.StudyGroupKeyedTag, c.Action);
        var inventory = Assert.Single(result.Neo4jTagReconciliationInventory);
        Assert.Equal(Neo4jTagReconciliationClassifications.VersionIdMatchedSqlTag, inventory.ReconciliationClassification);
        Assert.Equal(rowId, inventory.MatchedSqlTagRowId);
        Assert.False(inventory.IsQuarantined);
        Assert.DoesNotContain(result.Quarantine, q => q.Kind == Neo4jTagReconciliationClassifications.VersionIdMatchedSqlTag);
    }

    // 6. Neo4j tag without stableId and without a recognized id path -> quarantine.
    [Fact]
    public void Neo4jTag_NoStableId_NoRecognizedPath_IsQuarantined()
    {
        var neo = new[] { new Neo4jTagInput(StableId: null, Id: null,
            LinkedToKnowledgeNode: false, LinkedToStudyGroup: false, InContainHierarchy: false) };
        var result = Phase3DryRunEngine.Run(Inputs(neo: neo));

        var c = Assert.Single(result.Neo4jTagInventory);
        Assert.Equal(Phase3Actions.Quarantine, c.Action);
        var inventory = Assert.Single(result.Neo4jTagReconciliationInventory);
        Assert.Equal(Neo4jTagReconciliationClassifications.MissingAllSupportedIdentifiers, inventory.ReconciliationClassification);
        Assert.True(inventory.IsQuarantined);
        var q = Assert.Single(result.Quarantine, q => q.Kind == Neo4jTagReconciliationClassifications.MissingAllSupportedIdentifiers);
        Assert.Equal(QuarantineDispositions.InsufficientEvidence, q.RecommendedDisposition);
        Assert.Contains("stableId", q.AttemptedMatchingPaths!);
        Assert.Contains("id_as_sql_tag_row_id", q.AttemptedMatchingPaths!);
    }

    // 7. TagLevel/CONTAIN extraction -> candidate placement/relation PROPOSALS (Pending), not canonical.
    [Fact]
    public void Hierarchy_ExtractsCandidatePlacementsAndRelations_AsPendingProposals()
    {
        var node = Guid.NewGuid().ToString();
        var parent = Guid.NewGuid().ToString();
        var child = Guid.NewGuid().ToString();
        var result = Phase3DryRunEngine.Run(Inputs(
            levels: new[] { new TagLevelInput(node, ConceptLevels.Topic) },
            contains: new[] { new ContainEdgeInput(parent, child) }));

        var placement = Assert.Single(result.Hierarchy.PlacementCandidates);
        Assert.Equal(ConceptLevels.Topic, placement.Level);
        Assert.Equal(ProposalStatuses.Pending, placement.ProposalStatus);

        var relation = Assert.Single(result.Hierarchy.RelationCandidates);
        Assert.Equal(ConceptRelationTypes.BroaderThan, relation.RelationType); // CONTAIN -> broader_than, never related_to
        Assert.NotEqual(ConceptRelationTypes.RelatedTo, relation.RelationType);
        Assert.Equal(ProposalStatuses.Pending, relation.ProposalStatus);
        Assert.True(relation.BlockedFromDeterministicApplication);
        Assert.Contains("unresolved_endpoint", relation.BlockReasons!);

        // The output type is a proposal record — NOT the canonical EF ConceptPlacement/ConceptRelation entity.
        Assert.IsType<ConceptPlacementCandidateProposal>(placement);
        Assert.IsType<ConceptRelationCandidateProposal>(relation);
    }

    // 7b. Unknown level -> quarantine, not a placement.
    [Fact]
    public void Hierarchy_UnknownLevel_IsQuarantined()
    {
        var result = Phase3DryRunEngine.Run(Inputs(levels: new[] { new TagLevelInput(Guid.NewGuid().ToString(), "Galaxy") }));
        Assert.Empty(result.Hierarchy.PlacementCandidates);
        Assert.Contains(result.Quarantine, q => q.Kind == "neo4j_unknown_level");
    }

    // 8. Dry-run performs NO writes to legacy entities.
    [Fact]
    public void DryRun_PerformsNoWritesToLegacyEntities()
    {
        using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Tags.Add(new Tags { Id = Guid.NewGuid(), StableId = Guid.NewGuid(), Status = "Current", IsCurrent = true, Name = "统计力学" });
        db.SaveChanges();
        var before = db.Tags.Count();

        // Build inputs from the (read-only) tags and run the engine.
        var inputs = Inputs(sql: db.Tags.AsNoTracking()
            .Select(t => new LegacyTagInput(t.Id, t.StableId, t.Name, t.Status, t.IsCurrent, false)).ToList());
        _ = Phase3DryRunEngine.Run(inputs);

        Assert.Equal(before, db.Tags.Count());
        Assert.Empty(db.ChangeTracker.Entries().Where(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted));
    }

    // 9. Generated output shape is stable (deterministic) across runs.
    [Fact]
    public void OutputShape_IsStable_AcrossRuns()
    {
        var sid = Guid.NewGuid();
        var inputs = Inputs(
            sql: new[] { Tag("入门"), Tag("机器学习", sid), Tag("宇宙学 23a78f8d576080e090e9ecac150a51c5") },
            neo: new[] { new Neo4jTagInput(sid.ToString("N"), null, true, false, true) },
            levels: new[] { new TagLevelInput(Guid.NewGuid().ToString(), ConceptLevels.Field) },
            contains: new[] { new ContainEdgeInput(Guid.NewGuid().ToString(), Guid.NewGuid().ToString()) });

        var a = Phase3ArtifactWriter.Serialize(Phase3DryRunEngine.Run(inputs));
        var b = Phase3ArtifactWriter.Serialize(Phase3DryRunEngine.Run(inputs));

        var expected = new[]
        {
            "legacy_tag_inventory.json", "neo4j_tag_inventory.json", "legacy_tag_quarantine.json",
            "neo4j_hierarchy_extraction.json", "neo4j_hierarchy_diagnostics.json",
            "concept_candidate_proposals.json", "tag_value_candidate_proposals.json",
            "concept_placement_candidate_proposals.json", "concept_relation_candidate_proposals.json",
            "phase3_summary.json"
        };
        Assert.Equal(expected.OrderBy(x => x), a.Keys.OrderBy(x => x));
        foreach (var k in expected) Assert.Equal(a[k], b[k]); // byte-for-byte stable
    }

    // Operational seed term -> deterministic operational bucket + TagValue candidate (no Concept needed).
    [Fact]
    public void OperationalSeedTerm_ClassifiesAsOperational_WithTagValueCandidate()
    {
        var result = Phase3DryRunEngine.Run(Inputs(sql: new[] { Tag("入门") }));
        var c = Assert.Single(result.LegacyTagInventory);
        Assert.Equal(Phase3Buckets.PedagogicalTag, c.Bucket);
        var tv = Assert.Single(result.TagValueCandidates);
        Assert.Equal("difficulty", tv.Facet);
        Assert.Empty(result.ConceptCandidates); // operational tags need no Concept mapping
    }

    [Fact]
    public void Neo4jTag_StableIdCurrentSqlMatch_IsSqlBacked()
    {
        var stableId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var result = Phase3DryRunEngine.Run(Inputs(
            sql: new[] { Tag("化学", stableId, rowId) },
            neo: new[] { new Neo4jTagInput(stableId.ToString("N"), null, true, false, true) },
            sqlVersions: new[] { SqlVersion(rowId, stableId) }));

        var inventory = Assert.Single(result.Neo4jTagReconciliationInventory);
        Assert.Equal(Neo4jTagReconciliationClassifications.StableIdMatchedCurrentSqlTag, inventory.ReconciliationClassification);
        Assert.Equal(Neo4jTagKeyingPaths.StableId, inventory.DetectedKeyingPath);
        Assert.False(inventory.IsQuarantined);
        Assert.Equal(1, result.Summary.StableIdKeyedMatches);
        Assert.Equal(0, result.Summary.SqlCurrentTagsAbsentFromNeo4j);
    }

    [Fact]
    public void Neo4jTag_StableIdNoncurrentSqlMatch_IsReviewOnlyNotNeo4jOnly()
    {
        var stableId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var result = Phase3DryRunEngine.Run(Inputs(
            neo: new[] { new Neo4jTagInput(stableId.ToString(), null, true, false, true) },
            sqlVersions: new[] { SqlVersion(rowId, stableId, isCurrent: false, status: "Archived") }));

        var c = Assert.Single(result.Neo4jTagInventory);
        Assert.Equal(Phase3Actions.NeedsReview, c.Action);
        var inventory = Assert.Single(result.Neo4jTagReconciliationInventory);
        Assert.Equal(Neo4jTagReconciliationClassifications.StableIdMatchedNoncurrentSqlTag, inventory.ReconciliationClassification);
        Assert.False(inventory.IsQuarantined);
        Assert.Equal("Archived", inventory.MatchedSqlStatus);
        Assert.Equal(0, result.Summary.Neo4jOnlyNodes);
    }

    [Fact]
    public void Neo4jTag_ValidVersionIdMatchWithoutStableId_IsNotQuarantined()
    {
        var rowId = Guid.NewGuid();
        var stableId = Guid.NewGuid();
        var result = Phase3DryRunEngine.Run(Inputs(
            neo: new[] { new Neo4jTagInput(null, rowId.ToString(), false, true, false) },
            sqlVersions: new[] { SqlVersion(rowId, stableId) }));

        var inventory = Assert.Single(result.Neo4jTagReconciliationInventory);
        Assert.Equal(Neo4jTagReconciliationClassifications.VersionIdMatchedSqlTag, inventory.ReconciliationClassification);
        Assert.Equal(Neo4jTagKeyingPaths.VersionId, inventory.DetectedKeyingPath);
        Assert.False(inventory.IsQuarantined);
        Assert.Empty(result.Quarantine);
    }

    [Fact]
    public void Neo4jTag_InvalidIdentifierFormat_IsExplicitlyQuarantined()
    {
        var result = Phase3DryRunEngine.Run(Inputs(
            neo: new[] { new Neo4jTagInput("not-a-guid", null, true, false, false) }));

        var inventory = Assert.Single(result.Neo4jTagReconciliationInventory);
        Assert.Equal(Neo4jTagReconciliationClassifications.InvalidIdentifierFormat, inventory.ReconciliationClassification);
        Assert.True(inventory.IsQuarantined);
        var q = Assert.Single(result.Quarantine);
        Assert.Equal(QuarantineDispositions.InvalidNode, q.RecommendedDisposition);
    }

    [Fact]
    public void Neo4jTag_AmbiguousMultipleCurrentSqlMatches_IsExplicitlyQuarantined()
    {
        var stableId = Guid.NewGuid();
        var result = Phase3DryRunEngine.Run(Inputs(
            neo: new[] { new Neo4jTagInput(stableId.ToString(), null, true, false, false) },
            sqlVersions: new[]
            {
                SqlVersion(Guid.NewGuid(), stableId),
                SqlVersion(Guid.NewGuid(), stableId)
            }));

        var inventory = Assert.Single(result.Neo4jTagReconciliationInventory);
        Assert.Equal(Neo4jTagReconciliationClassifications.AmbiguousMultipleMatches, inventory.ReconciliationClassification);
        Assert.True(inventory.IsQuarantined);
        Assert.Equal(1, result.Summary.AmbiguousNodes);
    }

    [Fact]
    public void Neo4jTag_StableIdNotFound_IsNeo4jOnlyWithDeterministicRecoveryPath()
    {
        var stableId = Guid.NewGuid();
        var result = Phase3DryRunEngine.Run(Inputs(
            neo: new[] { new Neo4jTagInput(stableId.ToString(), null, true, false, false) }));

        Assert.Equal(1, result.Summary.Neo4jOnlyNodes);
        var q = Assert.Single(result.Quarantine);
        Assert.Equal(Neo4jTagReconciliationClassifications.StableIdNotFoundInSql, q.Kind);
        Assert.True(q.DeterministicRecoveryPossible);
        Assert.Equal(QuarantineDispositions.RecoverByStableId, q.RecommendedDisposition);
    }

    [Fact]
    public void Summary_ReconciliationInvariantsAndL10nCoverage_AreReported()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var result = Phase3DryRunEngine.Run(Inputs(
            sql: new[] { TagWithL10n("A", true, a), TagWithL10n("B", false, b) },
            neo: new[] { new Neo4jTagInput(a.ToString(), null, true, false, false) }));

        Assert.NotNull(result.Summary.L10nCoverage);
        Assert.Equal(2, result.Summary.L10nCoverage!.CurrentSqlTagRows);
        Assert.Equal(1, result.Summary.L10nCoverage.L10nBackedCurrentTags);
        Assert.Equal(1, result.Summary.L10nCoverage.NonL10nBackedCurrentTags);
        Assert.All(result.Summary.InvariantChecks!, check => Assert.True(check.Passed, check.Detail));
        Assert.Equal(1, result.Summary.SqlCurrentTagsAbsentFromNeo4j);
    }

    [Fact]
    public void Hierarchy_DuplicateEdges_AreDedupeDiagnosed()
    {
        var parent = Guid.NewGuid().ToString();
        var child = Guid.NewGuid().ToString();
        var result = Phase3DryRunEngine.Run(Inputs(
            sqlVersions: new[]
            {
                SqlVersion(Guid.NewGuid(), Guid.Parse(parent)),
                SqlVersion(Guid.NewGuid(), Guid.Parse(child))
            },
            contains: new[]
            {
                new ContainEdgeInput(parent, child, "r1"),
                new ContainEdgeInput(parent, child, "r2")
            }));

        Assert.Equal(2, result.HierarchyDiagnostics.RawContainEdgeCount);
        Assert.Equal(1, result.HierarchyDiagnostics.DeduplicatedContainEdgeCount);
        Assert.Equal(1, result.HierarchyDiagnostics.DuplicateEdgeCount);
        Assert.Single(result.Hierarchy.RelationCandidates);
    }

    [Fact]
    public void Hierarchy_SelfLoop_IsDetectedAndBlocked()
    {
        var id = Guid.NewGuid();
        var result = Phase3DryRunEngine.Run(Inputs(
            sqlVersions: new[] { SqlVersion(Guid.NewGuid(), id) },
            contains: new[] { new ContainEdgeInput(id.ToString(), id.ToString()) }));

        Assert.Equal(1, result.HierarchyDiagnostics.SelfLoopCount);
        var relation = Assert.Single(result.Hierarchy.RelationCandidates);
        Assert.True(relation.BlockedFromDeterministicApplication);
        Assert.Contains("self_loop", relation.BlockReasons!);
    }

    [Fact]
    public void Hierarchy_TwoNodeCycle_IsDetectedAndBlocked()
    {
        var chemistryBond = Guid.NewGuid();
        var inorganicChemistry = Guid.NewGuid();
        var result = Phase3DryRunEngine.Run(Inputs(
            sqlVersions: new[]
            {
                SqlVersion(Guid.NewGuid(), chemistryBond, name: "化学键"),
                SqlVersion(Guid.NewGuid(), inorganicChemistry, name: "无机化学")
            },
            contains: new[]
            {
                new ContainEdgeInput(chemistryBond.ToString(), inorganicChemistry.ToString()),
                new ContainEdgeInput(inorganicChemistry.ToString(), chemistryBond.ToString())
            }));

        Assert.Equal(2, result.HierarchyDiagnostics.CyclicEdgeCount);
        Assert.NotEmpty(result.HierarchyDiagnostics.Cycles);
        Assert.All(result.Hierarchy.RelationCandidates, relation =>
        {
            Assert.True(relation.BlockedFromDeterministicApplication);
            Assert.Contains("cycle", relation.BlockReasons!);
        });
    }

    [Fact]
    public void ConceptCandidates_RemainLowConfidenceReviewOnly()
    {
        var result = Phase3DryRunEngine.Run(Inputs(sql: new[] { Tag("量子化学") }));

        var candidate = Assert.Single(result.ConceptCandidates);
        Assert.Equal(0.3, candidate.Confidence);
        Assert.Equal(ProposalStatuses.Pending, candidate.ProposalStatus);
        Assert.NotEqual(Guid.Empty, candidate.SourceLegacyTagStableId);
    }
}
