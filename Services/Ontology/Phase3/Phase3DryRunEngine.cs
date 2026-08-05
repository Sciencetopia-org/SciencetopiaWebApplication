using System.Collections.Generic;
using System.Linq;

namespace Sciencetopia.Services.Ontology.Phase3;

// Pure assembly of the dry-run result from collected inputs. Deterministic (stable ordering),
// no I/O, no DB/Neo4j, no mutation. Output is review material only.
public static class Phase3DryRunEngine
{
    public static Phase3DryRunResult Run(Phase3Inputs inputs, OperationalTermDictionary? dictionary = null)
    {
        var dict = dictionary ?? new OperationalTermDictionary();
        var sqlVersions = inputs.SqlTagVersions ?? inputs.SqlTags
            .Where(t => t.VersionId.HasValue)
            .Select(t => new SqlTagIdentityInput(t.VersionId!.Value, t.StableId, t.Name, t.Status, t.IsCurrent, t.HasDefaultL10nSet))
            .ToList();
        var sqlStableIds = sqlVersions.Select(t => t.StableId.ToString()).ToHashSet(System.StringComparer.OrdinalIgnoreCase);

        var legacy = LegacyTagClassifier.Classify(inputs.SqlTags, dict);
        var neo = Neo4jTagReconciler.Reconcile(inputs.Neo4jTags, sqlVersions);
        var hierarchy = HierarchyExtractor.Extract(inputs.TagLevels, inputs.ContainEdges, sqlStableIds);

        var quarantine = legacy.Quarantine
            .Concat(neo.Quarantine)
            .Concat(hierarchy.Quarantine)
            .OrderBy(q => q.Kind, System.StringComparer.Ordinal)
            .ThenBy(q => q.Subject, System.StringComparer.Ordinal)
            .ToList();

        var l10nBacked = inputs.SqlTags.Count(t => t.HasDefaultL10nSet);
        var l10nCoverage = new L10nCoverageSummary(
            inputs.SqlTags.Count,
            l10nBacked,
            inputs.SqlTags.Count - l10nBacked,
            inputs.SqlTags.Count - l10nBacked,
            inputs.SqlTags.Count == 0 ? 0 : (double)l10nBacked / inputs.SqlTags.Count * 100);

        var matchedCurrentStableIds = neo.Inventory
            .Where(i => i.MatchedSqlIsCurrent == true && i.MatchedSqlTagStableId.HasValue)
            .Select(i => i.MatchedSqlTagStableId!.Value)
            .ToHashSet();
        var currentSqlStableIds = inputs.SqlTags.Select(t => t.StableId).ToHashSet();
        var absentCurrent = currentSqlStableIds.Count(id => !matchedCurrentStableIds.Contains(id));

        var stableMultipleNeo4j = neo.Inventory
            .Where(i => i.MatchedSqlTagStableId.HasValue)
            .GroupBy(i => i.MatchedSqlTagStableId!.Value)
            .Count(g => g.Count() > 1);
        var rowMultipleNeo4j = neo.Inventory
            .Where(i => i.MatchedSqlTagRowId.HasValue)
            .GroupBy(i => i.MatchedSqlTagRowId!.Value)
            .Count(g => g.Count() > 1);

        var reconciliationClassCounts = neo.Inventory
            .GroupBy(i => i.ReconciliationClassification)
            .ToDictionary(g => g.Key, g => g.Count());
        int CountClass(string classification) => reconciliationClassCounts.TryGetValue(classification, out var count) ? count : 0;
        var stableMatches = CountClass(Neo4jTagReconciliationClassifications.StableIdMatchedCurrentSqlTag)
                            + CountClass(Neo4jTagReconciliationClassifications.StableIdMatchedNoncurrentSqlTag);
        var versionMatches = CountClass(Neo4jTagReconciliationClassifications.VersionIdMatchedSqlTag);
        var neo4jOnly = CountClass(Neo4jTagReconciliationClassifications.StableIdNotFoundInSql)
                        + CountClass(Neo4jTagReconciliationClassifications.VersionIdNotFoundInSql);
        var unresolved = CountClass(Neo4jTagReconciliationClassifications.MissingAllSupportedIdentifiers)
                         + CountClass(Neo4jTagReconciliationClassifications.InvalidIdentifierFormat);
        var ambiguous = CountClass(Neo4jTagReconciliationClassifications.AmbiguousMultipleMatches);
        var quarantineByReason = quarantine.GroupBy(q => q.Kind).ToDictionary(g => g.Key, g => g.Count());

        var invariantChecks = new List<Phase3InvariantCheck>
        {
            new(
                "neo4j_reconciliation_total",
                neo.Inventory.Count == inputs.Neo4jTags.Count,
                $"{neo.Inventory.Count} inventory records for {inputs.Neo4jTags.Count} Neo4j tag inputs"),
            new(
                "neo4j_classification_total",
                reconciliationClassCounts.Values.Sum() == inputs.Neo4jTags.Count,
                $"{reconciliationClassCounts.Values.Sum()} classified Neo4j tag records for {inputs.Neo4jTags.Count} inputs"),
            new(
                "quarantine_total_by_reason",
                quarantineByReason.Values.Sum() == quarantine.Count,
                $"{quarantineByReason.Values.Sum()} grouped quarantine records for {quarantine.Count} total"),
            new(
                "sql_current_absent_nonnegative",
                absentCurrent >= 0,
                $"{absentCurrent} SQL current tags absent from matched Neo4j inventory")
        };

        var summary = new Phase3Summary(
            SqlTagsTotal: inputs.SqlTags.Count,
            Neo4jTagsTotal: inputs.Neo4jTags.Count,
            LegacyBucketCounts: legacy.Classifications.GroupBy(c => c.Bucket)
                .ToDictionary(g => g.Key, g => g.Count()),
            Neo4jBucketCounts: neo.Classifications.GroupBy(c => c.Bucket)
                .ToDictionary(g => g.Key, g => g.Count()),
            QuarantineCount: quarantine.Count,
            ConceptCandidateCount: legacy.ConceptCandidates.Count,
            TagValueCandidateCount: legacy.TagValueCandidates.Count,
            PlacementCandidateCount: hierarchy.Extraction.PlacementCandidates.Count,
            RelationCandidateCount: hierarchy.Extraction.RelationCandidates.Count,
            Note: "DRY-RUN ONLY. All proposals are Pending. No legacy data, Neo4j, or canonical ontology was modified.",
            SqlCurrentTagRows: inputs.SqlTags.Count,
            SqlTotalTagVersionRowsUsedForReconciliation: sqlVersions.Count,
            StableIdKeyedMatches: stableMatches,
            VersionIdKeyedMatches: versionMatches,
            Neo4jOnlyNodes: neo4jOnly,
            UnresolvedNodes: unresolved,
            AmbiguousNodes: ambiguous,
            SqlCurrentTagsAbsentFromNeo4j: absentCurrent,
            SqlStableIdsWithMultipleNeo4jMatches: stableMultipleNeo4j,
            SqlRowIdsWithMultipleNeo4jMatches: rowMultipleNeo4j,
            QuarantineByReasonCode: quarantineByReason,
            L10nCoverage: l10nCoverage,
            InvariantChecks: invariantChecks);

        return new Phase3DryRunResult(
            LegacyTagInventory: legacy.Classifications,
            Neo4jTagInventory: neo.Classifications,
            Neo4jTagReconciliationInventory: neo.Inventory,
            Quarantine: quarantine,
            Hierarchy: hierarchy.Extraction,
            HierarchyDiagnostics: hierarchy.Diagnostics,
            ConceptCandidates: legacy.ConceptCandidates,
            TagValueCandidates: legacy.TagValueCandidates,
            Summary: summary);
    }
}
