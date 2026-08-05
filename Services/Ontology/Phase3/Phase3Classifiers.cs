using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sciencetopia.Models.Ontology;

namespace Sciencetopia.Services.Ontology.Phase3;

// Deterministic, side-effect-free classifiers. No DB/Neo4j access here — they operate on the
// already-collected read-only inputs and return dry-run proposals/quarantine records only.
// Semantic buckets that require human/AI judgment (concept_exact vs metadata vs pedagogical for
// arbitrary names) are NOT assigned automatically (constraint #1/#7): clean tags become Unknown +
// a Pending concept candidate; only an explicit operational-term seed dictionary yields the
// pedagogical/metadata/community/workflow buckets deterministically.

/// <summary>Seed dictionary of well-known operational terms (deterministic, no AI). Maps a
/// normalized tag name to an operational bucket + facet + TagValue type. Override via ctor.</summary>
public sealed class OperationalTermDictionary
{
    private readonly IReadOnlyDictionary<string, (string Bucket, string Facet, string ValueType)> _map;

    public OperationalTermDictionary(IReadOnlyDictionary<string, (string, string, string)>? map = null)
        => _map = map ?? Default;

    public bool TryClassify(string normalizedName, out (string Bucket, string Facet, string ValueType) hit)
        => _map.TryGetValue(normalizedName, out hit);

    public static readonly IReadOnlyDictionary<string, (string Bucket, string Facet, string ValueType)> Default =
        new Dictionary<string, (string, string, string)>(StringComparer.Ordinal)
        {
            ["入门"] = (Phase3Buckets.PedagogicalTag, "difficulty", TagValueTypes.Pedagogical),
            ["进阶"] = (Phase3Buckets.PedagogicalTag, "difficulty", TagValueTypes.Pedagogical),
            ["初级"] = (Phase3Buckets.PedagogicalTag, "difficulty", TagValueTypes.Pedagogical),
            ["中级"] = (Phase3Buckets.PedagogicalTag, "difficulty", TagValueTypes.Pedagogical),
            ["高级"] = (Phase3Buckets.PedagogicalTag, "difficulty", TagValueTypes.Pedagogical),
            ["基础"] = (Phase3Buckets.PedagogicalTag, "difficulty", TagValueTypes.Pedagogical),
            ["中文"] = (Phase3Buckets.MetadataTag, "language", TagValueTypes.Metadata),
            ["英文"] = (Phase3Buckets.MetadataTag, "language", TagValueTypes.Metadata),
            ["双语"] = (Phase3Buckets.MetadataTag, "language", TagValueTypes.Metadata),
            ["免费视频"] = (Phase3Buckets.MetadataTag, "resource_type", TagValueTypes.Metadata),
            ["视频"] = (Phase3Buckets.MetadataTag, "resource_type", TagValueTypes.Metadata),
            ["书籍"] = (Phase3Buckets.MetadataTag, "resource_type", TagValueTypes.Metadata),
            ["读书会"] = (Phase3Buckets.CommunityTag, "community_type", TagValueTypes.Community),
            ["学习小组"] = (Phase3Buckets.CommunityTag, "community_type", TagValueTypes.Community),
            ["草稿"] = (Phase3Buckets.WorkflowTag, "workflow_status", TagValueTypes.Workflow),
            ["待审核"] = (Phase3Buckets.WorkflowTag, "workflow_status", TagValueTypes.Workflow),
            ["已发布"] = (Phase3Buckets.WorkflowTag, "workflow_status", TagValueTypes.Workflow),
        };
}

public sealed record LegacyClassificationOutput(
    IReadOnlyList<LegacyTagClassification> Classifications,
    IReadOnlyList<QuarantineRecord> Quarantine,
    IReadOnlyList<ConceptCandidateProposal> ConceptCandidates,
    IReadOnlyList<TagValueCandidateProposal> TagValueCandidates);

public static class LegacyTagClassifier
{
    // ^(.+?)\s+([a-fA-F0-9]{32})$  — dirty name like "宇宙学 23a78f8d576080e090e9ecac150a51c5".
    private static readonly Regex MalformedName =
        new(@"^(.+?)\s+([a-fA-F0-9]{32})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Returns (cleanName, hex32) if the name matches the dirty pattern, else null.</summary>
    public static (string CleanName, string Hex)? ParseMalformedName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var m = MalformedName.Match(name);
        if (!m.Success) return null;
        var clean = m.Groups[1].Value.Trim();
        if (clean.Length == 0) return null;
        return (clean, m.Groups[2].Value.ToLowerInvariant());
    }

    private static string Normalize(string? s) => (s ?? string.Empty).Trim().ToLowerInvariant();

    public static LegacyClassificationOutput Classify(IReadOnlyList<LegacyTagInput> tags, OperationalTermDictionary dict)
    {
        var classifications = new List<LegacyTagClassification>();
        var quarantine = new List<QuarantineRecord>();
        var conceptCandidates = new List<ConceptCandidateProposal>();
        var tagValueCandidates = new List<TagValueCandidateProposal>();

        // Cross-tag context.
        var dupStableIds = tags.GroupBy(t => t.StableId).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var existingStableIdHex = tags.Select(t => t.StableId.ToString("N").ToLowerInvariant()).ToHashSet();
        // Name collisions: same normalized clean name across DIFFERENT stable ids (clean tags only).
        var nameToStableIds = new Dictionary<string, HashSet<Guid>>();
        foreach (var t in tags)
        {
            if (string.IsNullOrWhiteSpace(t.Name)) continue;
            if (ParseMalformedName(t.Name) != null) continue;
            var n = Normalize(t.Name);
            if (!nameToStableIds.TryGetValue(n, out var set)) nameToStableIds[n] = set = new HashSet<Guid>();
            set.Add(t.StableId);
        }
        var collidingNames = nameToStableIds.Where(kv => kv.Value.Count > 1).Select(kv => kv.Key).ToHashSet();

        foreach (var t in tags.OrderBy(t => t.StableId))
        {
            string Pending = OntologyReviewStatuses.Pending;

            // 1) Blank/empty name -> garbage (delete PROPOSAL only).
            if (string.IsNullOrWhiteSpace(t.Name))
            {
                classifications.Add(new LegacyTagClassification(t.StableId, t.VersionId, t.Name,
                    Phase3Buckets.Garbage, Phase3Actions.DeleteProposal, "blank/empty tag name",
                    null, null, null, Pending, t.HasDefaultL10nSet));
                quarantine.Add(new QuarantineRecord("blank_name", t.StableId.ToString(), "blank/empty tag name", Pending));
                continue;
            }

            // 2) Malformed name -> dirty_legacy parse candidate (review required; never auto-applied).
            var parsed = ParseMalformedName(t.Name);
            if (parsed != null)
            {
                bool conflicts = existingStableIdHex.Contains(parsed.Value.Hex)
                                 && t.StableId.ToString("N").ToLowerInvariant() != parsed.Value.Hex;
                classifications.Add(new LegacyTagClassification(t.StableId, t.VersionId, t.Name,
                    Phase3Buckets.DirtyLegacy, Phase3Actions.DirtyLegacyParseCandidate,
                    conflicts ? "malformed name; parsed stableId CONFLICTS with an existing tag"
                              : "malformed name; parsed stableId has no conflict",
                    parsed.Value.CleanName, parsed.Value.Hex, conflicts, Pending, t.HasDefaultL10nSet));
                if (conflicts)
                    quarantine.Add(new QuarantineRecord("malformed_name_conflict", t.StableId.ToString(),
                        $"parsed stableId {parsed.Value.Hex} collides with an existing StableId", Pending));
                continue;
            }

            // 3) Missing StableId (empty-GUID sentinel) -> quarantine (no verified recovery path).
            if (t.StableId == Guid.Empty)
            {
                classifications.Add(new LegacyTagClassification(t.StableId, t.VersionId, t.Name,
                    Phase3Buckets.Unknown, Phase3Actions.Quarantine, "missing StableId (empty-GUID sentinel)",
                    null, null, null, Pending, t.HasDefaultL10nSet));
                quarantine.Add(new QuarantineRecord("missing_stableid", t.VersionId?.ToString() ?? t.Name!,
                    "tag has no StableId; no deterministic recovery path", Pending));
                continue;
            }

            // 4) Duplicate StableId among current tags -> quarantine / merge proposal (NO auto-merge).
            if (dupStableIds.Contains(t.StableId))
            {
                classifications.Add(new LegacyTagClassification(t.StableId, t.VersionId, t.Name,
                    Phase3Buckets.Unknown, Phase3Actions.MergeProposal, "duplicate current StableId",
                    null, null, null, Pending, t.HasDefaultL10nSet));
                quarantine.Add(new QuarantineRecord("duplicate_stableid", t.StableId.ToString(),
                    "more than one current row shares this StableId; manual merge review", Pending));
                continue;
            }

            // 5) Same normalized name, different StableId -> alias/duplicate PROPOSAL (NO auto-merge).
            if (collidingNames.Contains(Normalize(t.Name)))
            {
                classifications.Add(new LegacyTagClassification(t.StableId, t.VersionId, t.Name,
                    Phase3Buckets.Unknown, Phase3Actions.NameCollisionAliasCandidate,
                    "same normalized name shared by multiple StableIds", t.Name.Trim(), null, null, Pending, t.HasDefaultL10nSet));
                quarantine.Add(new QuarantineRecord("name_collision", Normalize(t.Name),
                    "ambiguous name across StableIds; alias/merge requires review (do NOT auto-merge)", Pending));
                continue;
            }

            // 6) Operational seed-dictionary hit -> deterministic operational bucket + TagValue candidate.
            if (dict.TryClassify(Normalize(t.Name), out var op))
            {
                classifications.Add(new LegacyTagClassification(t.StableId, t.VersionId, t.Name,
                    op.Bucket, Phase3Actions.OperationalTagValueCandidate, $"matched operational seed term ({op.Facet})",
                    t.Name.Trim(), null, null, Pending, t.HasDefaultL10nSet));
                tagValueCandidates.Add(new TagValueCandidateProposal(t.StableId, t.Name.Trim(), op.Facet,
                    op.ValueType, ProposalStatuses.Pending, "operational tag (no Concept mapping required)"));
                continue;
            }

            // 7) Clean current tag, no stronger evidence -> Unknown + Pending concept candidate (review).
            //    Name match alone is a CANDIDATE, never an automatic concept (constraint #1).
            classifications.Add(new LegacyTagClassification(t.StableId, t.VersionId, t.Name,
                Phase3Buckets.Unknown, Phase3Actions.ConceptCandidate,
                "clean current tag; concept vs operational requires review", t.Name.Trim(), null, null, Pending, t.HasDefaultL10nSet));
            conceptCandidates.Add(new ConceptCandidateProposal(t.StableId, t.Name.Trim(), ProposalStatuses.Pending,
                0.3, "clean current tag — fresh Concept.StableId to be minted on approval (never reuse Tag.StableId)"));
        }

        return new LegacyClassificationOutput(classifications, quarantine, conceptCandidates, tagValueCandidates);
    }
}

public sealed record Neo4jReconciliationOutput(
    IReadOnlyList<Neo4jTagClassification> Classifications,
    IReadOnlyList<Neo4jTagInventoryRecord> Inventory,
    IReadOnlyList<QuarantineRecord> Quarantine);

public static class Neo4jTagReconciler
{
    public static Neo4jReconciliationOutput Reconcile(
        IReadOnlyList<Neo4jTagInput> neoTags,
        IReadOnlyList<SqlTagIdentityInput> sqlTagVersions)
    {
        var classifications = new List<Neo4jTagClassification>();
        var inventory = new List<Neo4jTagInventoryRecord>();
        var quarantine = new List<QuarantineRecord>();

        var byStable = sqlTagVersions
            .GroupBy(t => t.StableId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var byVersionId = sqlTagVersions
            .Where(t => t.VersionId.HasValue)
            .GroupBy(t => t.VersionId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var t in neoTags.OrderBy(t => DiagnosticIdFor(t), StringComparer.Ordinal))
        {
            var diagnosticId = DiagnosticIdFor(t);
            var labels = t.Labels?.OrderBy(x => x, StringComparer.Ordinal).ToList() ?? new List<string> { "Tag" };
            var properties = t.Properties ?? new Dictionary<string, object?>();
            var evidence = BuildEvidence(t);
            var attempts = new List<string>();

            string keyingPath;
            string classification;
            string reason;
            Guid? matchedRowId = null;
            Guid? matchedStableId = null;
            bool? matchedCurrent = null;
            string? matchedStatus = null;
            bool quarantineNode = false;
            string disposition = QuarantineDispositions.ManualReview;
            bool deterministicRecovery = false;
            if (!string.IsNullOrWhiteSpace(t.StableId))
            {
                attempts.Add("stableId");
                if (!Guid.TryParse(t.StableId, out var stableGuid))
                {
                    keyingPath = Neo4jTagKeyingPaths.Invalid;
                    classification = Neo4jTagReconciliationClassifications.InvalidIdentifierFormat;
                    reason = "Neo4j :Tag stableId is present but is not a valid Guid.";
                    quarantineNode = true;
                    disposition = QuarantineDispositions.InvalidNode;
                }
                else if (!byStable.TryGetValue(stableGuid, out var matches) || matches.Count == 0)
                {
                    keyingPath = Neo4jTagKeyingPaths.StableId;
                    classification = Neo4jTagReconciliationClassifications.StableIdNotFoundInSql;
                    reason = "Neo4j :Tag stableId was not found in SQL Tags.";
                    quarantineNode = true;
                    disposition = QuarantineDispositions.RecoverByStableId;
                    deterministicRecovery = true;
                }
                else
                {
                    var current = matches.Where(m => m.IsCurrent).ToList();
                    if (current.Count > 1)
                    {
                        keyingPath = Neo4jTagKeyingPaths.Ambiguous;
                        classification = Neo4jTagReconciliationClassifications.AmbiguousMultipleMatches;
                        reason = "Neo4j :Tag stableId matched multiple current SQL Tag rows.";
                        quarantineNode = true;
                    }
                    else
                    {
                        var match = current.FirstOrDefault() ?? matches.OrderByDescending(m => m.IsCurrent).ThenBy(m => m.VersionId).First();
                        keyingPath = Neo4jTagKeyingPaths.StableId;
                        classification = match.IsCurrent
                            ? Neo4jTagReconciliationClassifications.StableIdMatchedCurrentSqlTag
                            : Neo4jTagReconciliationClassifications.StableIdMatchedNoncurrentSqlTag;
                        reason = match.IsCurrent
                            ? "Neo4j :Tag stableId matched a current SQL Tag."
                            : "Neo4j :Tag stableId matched only non-current SQL Tag version(s).";
                        matchedRowId = match.VersionId;
                        matchedStableId = match.StableId;
                        matchedCurrent = match.IsCurrent;
                        matchedStatus = match.Status;
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(t.Id))
            {
                attempts.Add("id_as_sql_tag_row_id");
                if (!Guid.TryParse(t.Id, out var rowGuid))
                {
                    keyingPath = Neo4jTagKeyingPaths.Invalid;
                    classification = Neo4jTagReconciliationClassifications.InvalidIdentifierFormat;
                    reason = "Neo4j :Tag id is present but is not a valid SQL Tag row Guid.";
                    quarantineNode = true;
                    disposition = QuarantineDispositions.InvalidNode;
                }
                else if (!byVersionId.TryGetValue(rowGuid, out var matches) || matches.Count == 0)
                {
                    keyingPath = Neo4jTagKeyingPaths.VersionId;
                    classification = Neo4jTagReconciliationClassifications.VersionIdNotFoundInSql;
                    reason = "Neo4j :Tag id did not match any SQL Tag row Id.";
                    quarantineNode = true;
                    disposition = QuarantineDispositions.RecoverByVersionId;
                    deterministicRecovery = true;
                }
                else if (matches.Count > 1)
                {
                    keyingPath = Neo4jTagKeyingPaths.Ambiguous;
                    classification = Neo4jTagReconciliationClassifications.AmbiguousMultipleMatches;
                    reason = "Neo4j :Tag id matched multiple SQL Tag rows.";
                    quarantineNode = true;
                }
                else
                {
                    var match = matches[0];
                    keyingPath = Neo4jTagKeyingPaths.VersionId;
                    classification = Neo4jTagReconciliationClassifications.VersionIdMatchedSqlTag;
                    reason = "Neo4j :Tag id matched SQL Tag row Id.";
                    matchedRowId = match.VersionId;
                    matchedStableId = match.StableId;
                    matchedCurrent = match.IsCurrent;
                    matchedStatus = match.Status;
                }
            }
            else
            {
                attempts.Add("stableId");
                attempts.Add("id_as_sql_tag_row_id");
                keyingPath = Neo4jTagKeyingPaths.Missing;
                classification = Neo4jTagReconciliationClassifications.MissingAllSupportedIdentifiers;
                reason = "Neo4j :Tag has neither stableId nor SQL Tag row id.";
                quarantineNode = true;
                disposition = QuarantineDispositions.InsufficientEvidence;
            }

            var bucket = quarantineNode ? Phase3Buckets.Unknown : "projected";
            var action = classification switch
            {
                Neo4jTagReconciliationClassifications.VersionIdMatchedSqlTag => Phase3Actions.StudyGroupKeyedTag,
                Neo4jTagReconciliationClassifications.StableIdMatchedCurrentSqlTag => Phase3Actions.ProjectedOk,
                Neo4jTagReconciliationClassifications.StableIdMatchedNoncurrentSqlTag => Phase3Actions.NeedsReview,
                _ => Phase3Actions.Quarantine
            };

            classifications.Add(new Neo4jTagClassification(t.StableId, t.Id, bucket, action, reason));
            inventory.Add(new Neo4jTagInventoryRecord(
                diagnosticId,
                t.StableId,
                t.Id,
                t.Name,
                labels,
                properties,
                evidence,
                keyingPath,
                matchedRowId,
                matchedStableId,
                matchedCurrent,
                matchedStatus,
                classification,
                reason,
                quarantineNode));

            if (quarantineNode)
            {
                quarantine.Add(new QuarantineRecord(
                    classification,
                    diagnosticId,
                    reason,
                    OntologyReviewStatuses.Pending,
                    diagnosticId,
                    t.StableId,
                    t.Id,
                    t.Name,
                    labels,
                    evidence,
                    attempts,
                    deterministicRecovery,
                    disposition));
            }

        }

        return new Neo4jReconciliationOutput(classifications, inventory, quarantine);
    }

    private static string DiagnosticIdFor(Neo4jTagInput t)
        => !string.IsNullOrWhiteSpace(t.ElementId)
            ? t.ElementId!
            : $"tag:{t.StableId ?? t.Id ?? "(missing-identifiers)"}";

    private static IReadOnlyList<string> BuildEvidence(Neo4jTagInput t)
    {
        var evidence = new List<string>();
        if (!string.IsNullOrWhiteSpace(t.StableId)) evidence.Add("has_stableId");
        if (!string.IsNullOrWhiteSpace(t.Id)) evidence.Add("has_id");
        if (t.LinkedToKnowledgeNode) evidence.Add("linked_to_knowledge_node");
        if (t.LinkedToStudyGroup) evidence.Add("linked_to_study_group");
        if (t.InContainHierarchy) evidence.Add("in_contain_hierarchy");
        if (t.Labels is { Count: > 0 }) evidence.Add($"labels:{string.Join(",", t.Labels.OrderBy(x => x, StringComparer.Ordinal))}");
        if (!evidence.Any()) evidence.Add("no_supported_identity_or_relationship_evidence");
        return evidence;
    }
}

public static class Neo4jTagClassifier
{
    public static (IReadOnlyList<Neo4jTagClassification> Classifications, IReadOnlyList<QuarantineRecord> Quarantine)
        Classify(IReadOnlyList<Neo4jTagInput> tags)
    {
        var classifications = new List<Neo4jTagClassification>();
        var quarantine = new List<QuarantineRecord>();

        foreach (var t in tags.OrderBy(t => t.StableId ?? t.Id ?? string.Empty, StringComparer.Ordinal))
        {
            bool hasStable = !string.IsNullOrWhiteSpace(t.StableId);
            bool hasId = !string.IsNullOrWhiteSpace(t.Id);

            if (hasStable)
            {
                if (!t.LinkedToKnowledgeNode && !t.InContainHierarchy && !t.LinkedToStudyGroup)
                {
                    classifications.Add(new Neo4jTagClassification(t.StableId, t.Id, Phase3Buckets.Garbage,
                        Phase3Actions.Neo4jOrphanTag, "isolated :Tag (no node, no hierarchy, no group)"));
                    quarantine.Add(new QuarantineRecord("neo4j_orphan_tag", t.StableId!,
                        "fully isolated Neo4j :Tag; delete proposal only", OntologyReviewStatuses.Pending));
                }
                else
                {
                    classifications.Add(new Neo4jTagClassification(t.StableId, t.Id, "projected",
                        Phase3Actions.ProjectedOk, "stableId-keyed projected tag"));
                }
                continue;
            }

            // No stableId: distinguish the expected StudyGroup keying from genuine anomalies (constraint #4).
            if (hasId && t.LinkedToStudyGroup)
            {
                classifications.Add(new Neo4jTagClassification(t.StableId, t.Id, "studygroup_keyed",
                    Phase3Actions.StudyGroupKeyedTag,
                    "StudyGroup-keyed legacy tag (expected; keyed by SQL version Id) — NOT dirty"));
                continue;
            }

            classifications.Add(new Neo4jTagClassification(t.StableId, t.Id, Phase3Buckets.Unknown,
                Phase3Actions.Neo4jTagMissingStableIdUnknown,
                "Neo4j :Tag without stableId and no recognized StudyGroup id path"));
            quarantine.Add(new QuarantineRecord("neo4j_tag_missing_stableId_unknown", t.Id ?? "(no id)",
                "Neo4j :Tag missing stableId and not a recognized StudyGroup tag", OntologyReviewStatuses.Pending));
        }

        return (classifications, quarantine);
    }
}

public sealed record HierarchyExtractionOutput(
    Neo4jHierarchyExtraction Extraction,
    Neo4jHierarchyDiagnostics Diagnostics,
    IReadOnlyList<QuarantineRecord> Quarantine);

public static class HierarchyExtractor
{
    // Neo4j-only level + hierarchy -> candidate placement/relation PROPOSALS (Pending). Never applied
    // to canonical ConceptPlacement/ConceptRelation automatically. CONTAIN maps to broader_than
    // (a real semantic relation), never the dangerous catch-all related_to (constraint #6).
    public static HierarchyExtractionOutput Extract(
        IReadOnlyList<TagLevelInput> tagLevels,
        IReadOnlyList<ContainEdgeInput> containEdges,
        IReadOnlySet<string>? sqlTagStableIds = null)
    {
        var placements = new List<ConceptPlacementCandidateProposal>();
        var relations = new List<ConceptRelationCandidateProposal>();
        var rawEdges = new List<RawContainEdgeDiagnostic>();
        var quarantine = new List<QuarantineRecord>();
        var validLevels = ConceptLevels.All.ToHashSet(StringComparer.Ordinal);

        foreach (var tl in tagLevels.OrderBy(x => x.NodeStableId, StringComparer.Ordinal).ThenBy(x => x.Level))
        {
            if (validLevels.Contains(tl.Level))
                placements.Add(new ConceptPlacementCandidateProposal(tl.NodeStableId, tl.Level,
                    ProposalStatuses.Pending, 0.9,
                    "extracted from Neo4j (:TagLevel)-[:TAGGED_WITH]->(:KnowledgeNode)", "neo4j_taglevel"));
            else
                quarantine.Add(new QuarantineRecord("neo4j_unknown_level", tl.NodeStableId,
                    $"TagLevel '{tl.Level}' is not a recognized ConceptLevel", OntologyReviewStatuses.Pending));
        }

        var duplicateKeys = containEdges
            .GroupBy(e => EdgeKey(e.ParentStableId, e.ChildStableId), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
        var uniqueEdges = containEdges
            .GroupBy(e => EdgeKey(e.ParentStableId, e.ChildStableId), StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(x => x.ParentStableId, StringComparer.Ordinal)
            .ThenBy(x => x.ChildStableId, StringComparer.Ordinal)
            .ToList();
        var cyclicEdgeKeys = DetectCyclicEdgeKeys(uniqueEdges);
        var cycles = BuildCycleDiagnostics(uniqueEdges);

        foreach (var e in containEdges.OrderBy(x => x.ParentStableId, StringComparer.Ordinal).ThenBy(x => x.ChildStableId, StringComparer.Ordinal))
        {
            var key = EdgeKey(e.ParentStableId, e.ChildStableId);
            var isSelfLoop = string.Equals(e.ParentStableId, e.ChildStableId, StringComparison.Ordinal);
            var isDuplicate = duplicateKeys.Contains(key);
            var isCyclic = cyclicEdgeKeys.Contains((e.ParentStableId, e.ChildStableId));
            var endpointsSqlBacked = EndpointBacked(e.ParentStableId, sqlTagStableIds)
                                     && EndpointBacked(e.ChildStableId, sqlTagStableIds);
            var blockReasons = new List<string>();
            if (isDuplicate) blockReasons.Add("duplicate_edge");
            if (isSelfLoop) blockReasons.Add("self_loop");
            if (isCyclic) blockReasons.Add("cycle");
            if (!endpointsSqlBacked) blockReasons.Add("unresolved_endpoint");
            rawEdges.Add(new RawContainEdgeDiagnostic(
                e.ParentStableId,
                e.ChildStableId,
                e.RelationshipElementId,
                e.ParentLabels?.ToList() ?? new List<string> { "Tag" },
                e.ChildLabels?.ToList() ?? new List<string> { "Tag" },
                isDuplicate,
                isSelfLoop,
                isCyclic,
                endpointsSqlBacked,
                blockReasons.Count == 0 ? "deterministic_structural_extraction" : "blocked_due_to_cycle_or_integrity",
                blockReasons));
        }

        foreach (var e in uniqueEdges)
        {
            var isSelfLoop = string.Equals(e.ParentStableId, e.ChildStableId, StringComparison.Ordinal);
            var isCyclic = cyclicEdgeKeys.Contains((e.ParentStableId, e.ChildStableId));
            var endpointsSqlBacked = EndpointBacked(e.ParentStableId, sqlTagStableIds)
                                     && EndpointBacked(e.ChildStableId, sqlTagStableIds);
            var blockReasons = new List<string>();
            if (isSelfLoop) blockReasons.Add("self_loop");
            if (isCyclic) blockReasons.Add("cycle");
            if (!endpointsSqlBacked) blockReasons.Add("unresolved_endpoint");

            if (isSelfLoop)
            {
                quarantine.Add(new QuarantineRecord("neo4j_contain_self_loop", e.ParentStableId,
                    "CONTAIN self-loop", OntologyReviewStatuses.Pending));
            }
            relations.Add(new ConceptRelationCandidateProposal(e.ParentStableId, e.ChildStableId,
                ConceptRelationTypes.BroaderThan, ProposalStatuses.Pending, 0.8,
                "extracted from Neo4j (:Tag)-[:CONTAIN]->(:Tag) — parent broader_than child", "neo4j_contain",
                blockReasons.Count > 0 ? "blocked_due_to_cycle_or_integrity" : "semantic_inference_requiring_human_review",
                blockReasons.Count > 0,
                blockReasons));
        }

        var diagnostics = new Neo4jHierarchyDiagnostics(
            containEdges.Count,
            uniqueEdges.Count,
            containEdges.Count - uniqueEdges.Count,
            rawEdges.Count(e => e.IsSelfLoop),
            rawEdges.Count(e => e.IsCyclic),
            0,
            rawEdges.Count(e => !e.EndpointsSqlBacked),
            rawEdges,
            cycles);

        return new HierarchyExtractionOutput(
            new Neo4jHierarchyExtraction(tagLevels.Count, containEdges.Count, placements, relations),
            diagnostics,
            quarantine);
    }

    private static string EdgeKey(string parent, string child) => $"{parent}\u001f{child}";

    private static bool EndpointBacked(string stableId, IReadOnlySet<string>? sqlTagStableIds)
        => sqlTagStableIds == null || sqlTagStableIds.Contains(stableId);

    private static HashSet<(string ParentStableId, string ChildStableId)> DetectCyclicEdgeKeys(IReadOnlyList<ContainEdgeInput> edges)
    {
        var adjacent = edges
            .GroupBy(e => e.ParentStableId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ChildStableId).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
        var cyclic = new HashSet<(string, string)>();

        foreach (var edge in edges)
        {
            if (string.Equals(edge.ParentStableId, edge.ChildStableId, StringComparison.Ordinal)
                || HasPath(edge.ChildStableId, edge.ParentStableId, adjacent))
            {
                cyclic.Add((edge.ParentStableId, edge.ChildStableId));
            }
        }

        return cyclic;
    }

    private static bool HasPath(string start, string target, IReadOnlyDictionary<string, List<string>> adjacent)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (string.Equals(current, target, StringComparison.Ordinal)) return true;
            if (!seen.Add(current)) continue;
            if (!adjacent.TryGetValue(current, out var next)) continue;
            foreach (var n in next) stack.Push(n);
        }

        return false;
    }

    private static IReadOnlyList<HierarchyCycleDiagnostic> BuildCycleDiagnostics(IReadOnlyList<ContainEdgeInput> edges)
    {
        var twoNodeCycles = edges
            .Where(e => edges.Any(o => string.Equals(o.ParentStableId, e.ChildStableId, StringComparison.Ordinal)
                                       && string.Equals(o.ChildStableId, e.ParentStableId, StringComparison.Ordinal)))
            .Select(e => new[] { e.ParentStableId, e.ChildStableId, e.ParentStableId })
            .Select(path => string.Join(">", path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(x => x.Split('>').ToList())
            .Select(path => new HierarchyCycleDiagnostic(path, path.Select(_ => (string?)null).ToList()))
            .ToList();

        return twoNodeCycles;
    }
}
