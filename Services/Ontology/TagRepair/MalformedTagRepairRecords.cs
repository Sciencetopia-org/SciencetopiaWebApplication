using System.Text.Json.Serialization;

namespace Sciencetopia.Services.Ontology.TagRepair;

public static class MalformedTagRepairActions
{
    public const string MergeIntoCanonicalNode = "merge_into_canonical_node";
    public const string RepairInPlace = "repair_in_place";
    public const string LeaveQuarantined = "leave_quarantined";
}

public static class RelationshipRepairActions
{
    public const string Create = "create";
    public const string SkipExactDuplicate = "skip_exact_duplicate";
}

public sealed record TagProjectionContract(
    string StableIdProperty,
    string CurrentVersionIdProperty,
    bool StoresName,
    string? NameProperty,
    IReadOnlyList<string> ProjectionOwnedLegacyProperties)
{
    public static TagProjectionContract Current { get; } = new(
        "stableId",
        "currentVersionId",
        false,
        null,
        new[] { "name", "status", "isCurrent" });
}

public sealed record RepairSqlTag(
    Guid? RowId,
    Guid StableId,
    string? Name,
    string? Status,
    bool IsCurrent);

public sealed record RepairGraphNode(
    string ElementId,
    IReadOnlyList<string> Labels,
    IReadOnlyDictionary<string, object?> Properties);

public sealed record RepairGraphRelationship(
    string ElementId,
    string Type,
    string StartNodeElementId,
    string EndNodeElementId,
    IReadOnlyDictionary<string, object?> Properties);

public sealed record RepairGraphSnapshot(
    IReadOnlyList<RepairGraphNode> Nodes,
    IReadOnlyList<RepairGraphRelationship> Relationships);

public sealed record PlannedRelationshipOperation(
    string SourceRelationshipElementId,
    string StartNodeElementId,
    string EndNodeElementId,
    string Type,
    IReadOnlyDictionary<string, object?> Properties,
    string Action,
    string ExactIdentityHash);

public sealed record MalformedTagRepairRecord(
    string Neo4jElementId,
    string? RawMalformedName,
    string? ExtractedSuffix,
    Guid? CanonicalGuid,
    Guid? MatchedSqlTagRowId,
    Guid? MatchedSqlTagStableId,
    bool? MatchedSqlIsCurrent,
    string? CanonicalSqlName,
    string? CanonicalNeo4jTagElementId,
    int IncomingRelationshipCount,
    int OutgoingRelationshipCount,
    IReadOnlyDictionary<string, int> RelationshipTypeDistribution,
    string PlannedAction,
    IReadOnlyList<string> BlockingReasons,
    int ExpectedRelationshipCreates,
    int ExpectedDuplicateRelationshipsSkipped,
    bool ExpectedMalformedNodeDeletion,
    string? ExpectedPostRepairNodeElementId,
    Guid? ExpectedPostRepairStableId);

public sealed record RepairNodeBackup(
    string ElementId,
    IReadOnlyList<string> Labels,
    IReadOnlyDictionary<string, object?> Properties,
    IReadOnlyList<RepairGraphRelationship> Relationships,
    IReadOnlyList<RepairGraphNode> EndpointNodes);

public sealed record MalformedTagRepairSummary(
    int PlannedRecordCount,
    int MergeIntoCanonicalCount,
    int RepairInPlaceCount,
    int LeaveQuarantinedCount,
    int BlockingValidationErrorCount,
    int ExpectedRelationshipCreates,
    int ExpectedDuplicateRelationshipsSkipped,
    int ExpectedMalformedNodeDeletions,
    string ProjectionContract);

public sealed record RepairVerificationPlan(
    int ExpectedSqlCurrentTagCount,
    int ExpectedMalformedNodeCountBefore,
    int ExpectedMalformedNodeCountAfter,
    int ExpectedRelationshipCountBefore,
    int ExpectedRelationshipCountAfter,
    int ExpectedExplicitStableIdCoverage,
    int ExpectedDuplicateExplicitStableIdCount,
    int ExpectedContainRelationshipCount,
    int ExpectedContainCycleEdgeCount,
    string ExpectedContainLogicalEdgeHash,
    string SqlSnapshotHash,
    string Neo4jSnapshotHash,
    IReadOnlyList<string> Postconditions);

public sealed record MalformedTagRepairPlan(
    string SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    string PlanHash,
    TagProjectionContract ProjectionContract,
    IReadOnlyList<MalformedTagRepairRecord> Records,
    IReadOnlyList<PlannedRelationshipOperation> RelationshipOperations,
    IReadOnlyList<RepairNodeBackup> Backup,
    MalformedTagRepairSummary Summary,
    RepairVerificationPlan VerificationPlan,
    IReadOnlyList<string> BlockingValidationErrors);

public sealed record MalformedTagRepairResult(
    string PlanHash,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    bool Succeeded,
    string Status,
    int PlannedRecords,
    int MergedIntoCanonicalNodes,
    int RepairedInPlace,
    int LeftQuarantined,
    int MalformedNodesDeleted,
    int RelationshipCountBefore,
    int RelationshipCountAfter,
    int RelationshipPropertiesVerified,
    int SqlCurrentTagCoverage,
    int SqlCurrentTagCount,
    int DuplicateExplicitStableIdCount,
    int UnresolvedMalformedNodeCount,
    bool HierarchyDiagnosticsConsistent,
    bool SqlWasReadOnly,
    IReadOnlyList<string> PostconditionFailures,
    string? FailureCode = null,
    string? FailureMessage = null);

public sealed record TagRepairApplyRequest(
    string PlanFilePath,
    string SuppliedPlanHash,
    bool ConfirmNeo4jWrite);

public sealed record TagRepairApplyOutcome(
    bool Succeeded,
    int ExitCode,
    MalformedTagRepairResult Result,
    string ResultPath);
