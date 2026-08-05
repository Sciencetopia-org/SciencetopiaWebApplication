using System;
using System.Collections.Generic;

namespace Sciencetopia.Services.Ontology.Phase3;

// Phase 3 — Neo4j→SQL extraction backlog + legacy-tag inventory / dry-run mapping.
// ALL types here describe READ-ONLY inputs and DRY-RUN outputs. Nothing in this namespace
// mutates legacy SQL, writes Neo4j, or creates canonical ontology rows. Every proposal is
// emitted as Pending only (reusing the Phase 1 status constants).

// ---------------- Inputs (collected read-only) ----------------

/// <summary>A current SQL Tag row (KnowledgeGraph.Tags). Name is authoritative for the
/// malformed-name check — Neo4j does not store names (audit §3 / diagnostics).</summary>
public sealed record LegacyTagInput(
    Guid? VersionId,        // Tags.Id  (row/version identity)
    Guid StableId,          // Tags.StableId (semantic identity)
    string? Name,           // Tags.Name
    string? Status,
    bool IsCurrent,
    bool HasDefaultL10nSet);

/// <summary>All SQL Tag versions used for Neo4j reconciliation. This is broader than the
/// current-tag inventory so version-id keyed Neo4j nodes can be matched safely.</summary>
public sealed record SqlTagIdentityInput(
    Guid? VersionId,
    Guid StableId,
    string? Name,
    string? Status,
    bool IsCurrent,
    bool HasDefaultL10nSet);

/// <summary>A Neo4j :Tag node. Two keying schemes exist (constraint #4):
/// knowledge/plan tags key on <see cref="StableId"/>; StudyGroup tags key on <see cref="Id"/>.</summary>
public sealed record Neo4jTagInput(
    string? StableId,
    string? Id,
    bool LinkedToKnowledgeNode,
    bool LinkedToStudyGroup,
    bool InContainHierarchy,
    string? ElementId = null,
    string? Name = null,
    IReadOnlyList<string>? Labels = null,
    IReadOnlyDictionary<string, object?>? Properties = null);

/// <summary>(:TagLevel {name=Level})-[:TAGGED_WITH]->(:KnowledgeNode {stableId=NodeStableId}).
/// This is the actual confirmed schema (audit §3); the level lives only in Neo4j today.</summary>
public sealed record TagLevelInput(string NodeStableId, string Level);

/// <summary>(:Tag {stableId=ParentStableId})-[:CONTAIN]->(:Tag {stableId=ChildStableId}).</summary>
public sealed record ContainEdgeInput(
    string ParentStableId,
    string ChildStableId,
    string? RelationshipElementId = null,
    IReadOnlyList<string>? ParentLabels = null,
    IReadOnlyList<string>? ChildLabels = null,
    IReadOnlyDictionary<string, object?>? ParentProperties = null,
    IReadOnlyDictionary<string, object?>? ChildProperties = null,
    IReadOnlyDictionary<string, object?>? RelationshipProperties = null);

public sealed record Phase3Inputs(
    IReadOnlyList<LegacyTagInput> SqlTags,
    IReadOnlyList<Neo4jTagInput> Neo4jTags,
    IReadOnlyList<TagLevelInput> TagLevels,
    IReadOnlyList<ContainEdgeInput> ContainEdges,
    IReadOnlyList<SqlTagIdentityInput>? SqlTagVersions = null);

// ---------------- Buckets / actions ----------------

public static class Phase3Buckets
{
    // The brief's legacy-tag categories.
    public const string ConceptExact = "concept_exact";
    public const string ConceptAlias = "concept_alias";
    public const string ConceptRelated = "concept_related";
    public const string MetadataTag = "metadata_tag";
    public const string PedagogicalTag = "pedagogical_tag";
    public const string CommunityTag = "community_tag";
    public const string WorkflowTag = "workflow_tag";
    public const string DirtyLegacy = "dirty_legacy";
    public const string Garbage = "garbage";
    public const string Unknown = "unknown";
}

public static class Phase3Actions
{
    public const string DirtyLegacyParseCandidate = "dirty_legacy_parse_candidate";
    public const string Quarantine = "quarantine";
    public const string MergeProposal = "merge_proposal";
    public const string NameCollisionAliasCandidate = "name_collision_alias_candidate";
    public const string DeleteProposal = "delete_proposal";
    public const string ConceptCandidate = "concept_candidate";
    public const string OperationalTagValueCandidate = "operational_tag_value_candidate";
    public const string NeedsReview = "needs_review";
    public const string ProjectedOk = "projected_ok";
    public const string StudyGroupKeyedTag = "studygroup_keyed_tag";
    public const string Neo4jTagMissingStableIdUnknown = "neo4j_tag_missing_stableId_unknown";
    public const string Neo4jOrphanTag = "neo4j_orphan_tag";
}

public static class Neo4jTagReconciliationClassifications
{
    public const string StableIdMatchedCurrentSqlTag = "stable_id_matched_current_sql_tag";
    public const string StableIdMatchedNoncurrentSqlTag = "stable_id_matched_noncurrent_sql_tag";
    public const string VersionIdMatchedSqlTag = "version_id_matched_sql_tag";
    public const string StableIdNotFoundInSql = "stable_id_not_found_in_sql";
    public const string VersionIdNotFoundInSql = "version_id_not_found_in_sql";
    public const string MissingAllSupportedIdentifiers = "missing_all_supported_identifiers";
    public const string AmbiguousMultipleMatches = "ambiguous_multiple_matches";
    public const string InvalidIdentifierFormat = "invalid_identifier_format";
}

public static class Neo4jTagKeyingPaths
{
    public const string StableId = "stableId";
    public const string VersionId = "version_id";
    public const string Missing = "missing_supported_identifier";
    public const string Invalid = "invalid_identifier";
    public const string Ambiguous = "ambiguous";
}

public static class QuarantineDispositions
{
    public const string RecoverByStableId = "recover_by_stable_id";
    public const string RecoverByVersionId = "recover_by_version_id";
    public const string ManualReview = "manual_review";
    public const string IgnoreExpectedLegacyProjection = "ignore_expected_legacy_projection";
    public const string InvalidNode = "invalid_node";
    public const string InsufficientEvidence = "insufficient_evidence";
}

// ---------------- Outputs (dry-run, all Pending) ----------------

public sealed record LegacyTagClassification(
    Guid StableId,
    Guid? VersionId,
    string? Name,
    string Bucket,
    string Action,
    string Reason,
    string? CandidateName,
    string? CandidateStableId,
    bool? ConflictsWithExistingStableId,
    string ReviewStatus,
    bool HasDefaultL10nSet = false);

public sealed record Neo4jTagClassification(
    string? StableId,
    string? Id,
    string Bucket,
    string Action,
    string Reason);

public sealed record QuarantineRecord(
    string Kind,
    string Subject,
    string Reason,
    string ReviewStatus,
    string? DiagnosticId = null,
    string? StableId = null,
    string? LegacyId = null,
    string? Name = null,
    IReadOnlyList<string>? Labels = null,
    IReadOnlyList<string>? ObservedEvidence = null,
    IReadOnlyList<string>? AttemptedMatchingPaths = null,
    bool DeterministicRecoveryPossible = false,
    string? RecommendedDisposition = null);

public sealed record Neo4jTagInventoryRecord(
    string DiagnosticId,
    string? StableId,
    string? LegacyId,
    string? Name,
    IReadOnlyList<string> Labels,
    IReadOnlyDictionary<string, object?> Properties,
    IReadOnlyList<string> ObservedEvidence,
    string DetectedKeyingPath,
    Guid? MatchedSqlTagRowId,
    Guid? MatchedSqlTagStableId,
    bool? MatchedSqlIsCurrent,
    string? MatchedSqlStatus,
    string ReconciliationClassification,
    string ReconciliationReason,
    bool IsQuarantined);

public sealed record ConceptCandidateProposal(
    Guid SourceLegacyTagStableId,
    string? CandidateLabel,
    string ProposalStatus,
    double Confidence,
    string Reason);

public sealed record TagValueCandidateProposal(
    Guid SourceLegacyTagStableId,
    string? Label,
    string Facet,
    string ValueType,
    string ProposalStatus,
    string Reason);

public sealed record ConceptPlacementCandidateProposal(
    string NodeStableId,
    string Level,
    string ProposalStatus,
    double Confidence,
    string Reason,
    string Source);

public sealed record ConceptRelationCandidateProposal(
    string FromStableId,
    string ToStableId,
    string RelationType,
    string ProposalStatus,
    double Confidence,
    string Reason,
    string Source,
    string Readiness = "semantic_inference_requiring_human_review",
    bool BlockedFromDeterministicApplication = false,
    IReadOnlyList<string>? BlockReasons = null);

public sealed record Neo4jHierarchyExtraction(
    int TagLevelAssignmentCount,
    int ContainEdgeCount,
    IReadOnlyList<ConceptPlacementCandidateProposal> PlacementCandidates,
    IReadOnlyList<ConceptRelationCandidateProposal> RelationCandidates);

public sealed record RawContainEdgeDiagnostic(
    string FromStableId,
    string ToStableId,
    string? RelationshipElementId,
    IReadOnlyList<string> ParentLabels,
    IReadOnlyList<string> ChildLabels,
    bool IsDuplicate,
    bool IsSelfLoop,
    bool IsCyclic,
    bool EndpointsSqlBacked,
    string Readiness,
    IReadOnlyList<string> BlockReasons);

public sealed record HierarchyCycleDiagnostic(
    IReadOnlyList<string> StableIdPath,
    IReadOnlyList<string?> NamePath);

public sealed record Neo4jHierarchyDiagnostics(
    int RawContainEdgeCount,
    int DeduplicatedContainEdgeCount,
    int DuplicateEdgeCount,
    int SelfLoopCount,
    int CyclicEdgeCount,
    int UnsupportedPatternCount,
    int UnresolvedEndpointEdgeCount,
    IReadOnlyList<RawContainEdgeDiagnostic> RawContainEdges,
    IReadOnlyList<HierarchyCycleDiagnostic> Cycles);

public sealed record L10nCoverageSummary(
    int CurrentSqlTagRows,
    int L10nBackedCurrentTags,
    int NonL10nBackedCurrentTags,
    int MissingDefaultL10nSetReference,
    double CoveragePercentage);

public sealed record Phase3InvariantCheck(string Name, bool Passed, string Detail);

public sealed record Phase3Summary(
    int SqlTagsTotal,
    int Neo4jTagsTotal,
    IReadOnlyDictionary<string, int> LegacyBucketCounts,
    IReadOnlyDictionary<string, int> Neo4jBucketCounts,
    int QuarantineCount,
    int ConceptCandidateCount,
    int TagValueCandidateCount,
    int PlacementCandidateCount,
    int RelationCandidateCount,
    string Note,
    int SqlCurrentTagRows = 0,
    int SqlTotalTagVersionRowsUsedForReconciliation = 0,
    int StableIdKeyedMatches = 0,
    int VersionIdKeyedMatches = 0,
    int Neo4jOnlyNodes = 0,
    int UnresolvedNodes = 0,
    int AmbiguousNodes = 0,
    int SqlCurrentTagsAbsentFromNeo4j = 0,
    int SqlStableIdsWithMultipleNeo4jMatches = 0,
    int SqlRowIdsWithMultipleNeo4jMatches = 0,
    IReadOnlyDictionary<string, int>? QuarantineByReasonCode = null,
    L10nCoverageSummary? L10nCoverage = null,
    IReadOnlyList<Phase3InvariantCheck>? InvariantChecks = null);

public sealed record Phase3DryRunResult(
    IReadOnlyList<LegacyTagClassification> LegacyTagInventory,
    IReadOnlyList<Neo4jTagClassification> Neo4jTagInventory,
    IReadOnlyList<Neo4jTagInventoryRecord> Neo4jTagReconciliationInventory,
    IReadOnlyList<QuarantineRecord> Quarantine,
    Neo4jHierarchyExtraction Hierarchy,
    Neo4jHierarchyDiagnostics HierarchyDiagnostics,
    IReadOnlyList<ConceptCandidateProposal> ConceptCandidates,
    IReadOnlyList<TagValueCandidateProposal> TagValueCandidates,
    Phase3Summary Summary);
