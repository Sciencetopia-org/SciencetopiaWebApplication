using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Sciencetopia.Data;

namespace Sciencetopia.Services.Ontology.TagRepair;

public sealed class MalformedTagRepairService
{
    private const string SnapshotCypher = @"
MATCH (t:Tag)
OPTIONAL MATCH (t)-[r]-(o)
RETURN elementId(t) AS tagElementId, labels(t) AS tagLabels, properties(t) AS tagProperties,
  CASE WHEN r IS NULL THEN null ELSE elementId(r) END AS relationshipElementId,
  CASE WHEN r IS NULL THEN null ELSE type(r) END AS relationshipType,
  CASE WHEN r IS NULL THEN null ELSE elementId(startNode(r)) END AS startElementId,
  CASE WHEN r IS NULL THEN null ELSE elementId(endNode(r)) END AS endElementId,
  CASE WHEN r IS NULL THEN null ELSE properties(r) END AS relationshipProperties,
  CASE WHEN o IS NULL THEN null ELSE elementId(o) END AS otherElementId,
  CASE WHEN o IS NULL THEN [] ELSE labels(o) END AS otherLabels,
  CASE WHEN o IS NULL THEN {} ELSE properties(o) END AS otherProperties";

    private readonly ApplicationDbContext _db;
    private readonly IDriver _driver;

    public MalformedTagRepairService(ApplicationDbContext db, IDriver driver)
    {
        _db = db;
        _driver = driver;
    }

    public async Task<(MalformedTagRepairPlan Plan, string OutputDirectory)> PlanAsync(
        string artifactsRoot,
        CancellationToken cancellationToken = default)
    {
        var sqlTags = await CollectSqlTagsAsync(cancellationToken);
        var snapshot = await CollectGraphSnapshotReadOnlyAsync();
        var plan = MalformedTagRepairPlanner.Build(sqlTags, snapshot);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        var outputDirectory = Path.Combine(artifactsRoot, "ontology_tag_repair", stamp);
        MalformedTagRepairArtifacts.WritePlan(plan, outputDirectory);
        return (plan, outputDirectory);
    }

    public async Task<TagRepairApplyOutcome> ApplyAsync(
        TagRepairApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        MalformedTagRepairPlan plan;
        try
        {
            plan = MalformedTagRepairArtifacts.ReadPlan(request.PlanFilePath);
        }
        catch (Exception ex)
        {
            var invalid = FailureResult(string.Empty, started, "invalid_plan_file", ex.Message, 0, 0);
            var invalidPath = MalformedTagRepairArtifacts.WriteResult(invalid, request.PlanFilePath);
            return new TagRepairApplyOutcome(false, 2, invalid, invalidPath);
        }

        var guard = MalformedTagRepairCli.ValidateApplyRequest(request, plan);
        if (!guard.IsValid)
        {
            var blocked = FailureResult(plan.PlanHash, started, "apply_guard_failed", string.Join(" ", guard.Errors),
                plan.Summary.PlannedRecordCount, plan.Summary.LeaveQuarantinedCount);
            var blockedPath = MalformedTagRepairArtifacts.WriteResult(blocked, request.PlanFilePath);
            return new TagRepairApplyOutcome(false, 2, blocked, blockedPath);
        }

        try
        {
            var sqlTags = await CollectSqlTagsAsync(cancellationToken);
            if (!string.Equals(MalformedTagRepairPlanner.ComputeSqlSnapshotHash(sqlTags), plan.VerificationPlan.SqlSnapshotHash,
                    StringComparison.Ordinal))
                throw new InvalidOperationException("stale_plan_sql_snapshot_mismatch");

            await using var session = _driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Write));
            var result = await session.ExecuteWriteAsync(async tx =>
            {
                var before = await CollectGraphSnapshotAsync(tx);
                if (!string.Equals(MalformedTagRepairPlanner.ComputeGraphSnapshotHash(before), plan.VerificationPlan.Neo4jSnapshotHash,
                        StringComparison.Ordinal))
                    throw new InvalidOperationException("stale_plan_neo4j_snapshot_mismatch");

                await RepairInPlaceAsync(tx, plan);
                await CreateRelationshipsAsync(tx, plan);
                var verifiedProperties = await VerifyPlannedRelationshipsAsync(tx, plan);
                if (!MalformedTagRepairTransactionPolicy.CanDeleteMalformedNodes(
                        plan.RelationshipOperations.Count, verifiedProperties))
                    throw new InvalidOperationException("relationship_verification_incomplete");
                await DeleteMergedNodesAsync(tx, plan);

                var after = await CollectGraphSnapshotAsync(tx);
                var failures = VerifyPostconditions(plan, sqlTags, after);
                if (failures.Count > 0)
                    throw new InvalidOperationException($"postcondition_failed:{string.Join(",", failures)}");

                return new MalformedTagRepairResult(
                    plan.PlanHash,
                    started,
                    DateTimeOffset.UtcNow,
                    true,
                    "applied_and_verified",
                    plan.Summary.PlannedRecordCount,
                    plan.Summary.MergeIntoCanonicalCount,
                    plan.Summary.RepairInPlaceCount,
                    plan.Summary.LeaveQuarantinedCount,
                    plan.Summary.ExpectedMalformedNodeDeletions,
                    before.Relationships.Count,
                    after.Relationships.Count,
                    verifiedProperties,
                    CountSqlCoverage(sqlTags, after),
                    plan.VerificationPlan.ExpectedSqlCurrentTagCount,
                    CountDuplicateStableIds(after),
                    CountMalformedNodes(after),
                    VerifyHierarchy(plan, after),
                    true,
                    Array.Empty<string>());
            });
            var resultPath = MalformedTagRepairArtifacts.WriteResult(result, request.PlanFilePath);
            return new TagRepairApplyOutcome(true, 0, result, resultPath);
        }
        catch (Exception ex)
        {
            var failed = FailureResult(plan.PlanHash, started, "transaction_rolled_back", SafeMessage(ex),
                plan.Summary.PlannedRecordCount, plan.Summary.LeaveQuarantinedCount);
            var failedPath = MalformedTagRepairArtifacts.WriteResult(failed, request.PlanFilePath);
            return new TagRepairApplyOutcome(false, 3, failed, failedPath);
        }
    }

    private async Task<IReadOnlyList<RepairSqlTag>> CollectSqlTagsAsync(CancellationToken cancellationToken)
    {
        var rows = await _db.Tags.AsNoTracking()
            .Where(t => t.IsCurrent)
            .Select(t => new { RowId = t.Id, t.StableId, t.Name, t.Status, t.IsCurrent })
            .ToListAsync(cancellationToken);
        return rows.Select(t => new RepairSqlTag(t.RowId, t.StableId, t.Name, t.Status, t.IsCurrent)).ToList();
    }

    private async Task<RepairGraphSnapshot> CollectGraphSnapshotReadOnlyAsync()
    {
        await using var session = _driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Read));
        return await session.ExecuteReadAsync(CollectGraphSnapshotAsync);
    }

    private static async Task<RepairGraphSnapshot> CollectGraphSnapshotAsync(IAsyncQueryRunner runner)
    {
        var cursor = await runner.RunAsync(SnapshotCypher);
        var rows = await cursor.ToListAsync();
        var nodes = new Dictionary<string, RepairGraphNode>(StringComparer.Ordinal);
        var relationships = new Dictionary<string, RepairGraphRelationship>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            AddNode(nodes, row["tagElementId"].As<string>(), row["tagLabels"].As<List<string>>(),
                row["tagProperties"].As<Dictionary<string, object>>());
            var otherId = row["otherElementId"].As<string?>();
            if (!string.IsNullOrWhiteSpace(otherId))
                AddNode(nodes, otherId, row["otherLabels"].As<List<string>>(), row["otherProperties"].As<Dictionary<string, object>>());
            var relationshipId = row["relationshipElementId"].As<string?>();
            if (string.IsNullOrWhiteSpace(relationshipId) || relationships.ContainsKey(relationshipId)) continue;
            relationships[relationshipId] = new RepairGraphRelationship(
                relationshipId,
                row["relationshipType"].As<string>(),
                row["startElementId"].As<string>(),
                row["endElementId"].As<string>(),
                NormalizeProperties(row["relationshipProperties"].As<Dictionary<string, object>>()));
        }
        return new RepairGraphSnapshot(
            nodes.Values.OrderBy(n => n.ElementId, StringComparer.Ordinal).ToList(),
            relationships.Values.OrderBy(r => r.ElementId, StringComparer.Ordinal).ToList());
    }

    private static void AddNode(Dictionary<string, RepairGraphNode> nodes, string id, IReadOnlyList<string> labels,
        IDictionary<string, object> properties)
    {
        if (!nodes.ContainsKey(id))
            nodes[id] = new RepairGraphNode(id, labels.OrderBy(x => x, StringComparer.Ordinal).ToList(), NormalizeProperties(properties));
    }

    private static async Task RepairInPlaceAsync(IAsyncQueryRunner tx, MalformedTagRepairPlan plan)
    {
        foreach (var record in plan.Records.Where(r => r.PlannedAction == MalformedTagRepairActions.RepairInPlace))
        {
            var cursor = await tx.RunAsync(@"
MATCH (t:Tag) WHERE elementId(t) = $elementId
SET t.stableId = $stableId, t.currentVersionId = $currentVersionId
REMOVE t.name, t.status, t.isCurrent
RETURN count(t) AS changed", new
            {
                elementId = record.Neo4jElementId,
                stableId = record.MatchedSqlTagStableId!.Value.ToString(),
                currentVersionId = record.MatchedSqlTagRowId!.Value.ToString()
            });
            var changed = await cursor.SingleAsync(r => r["changed"].As<long>());
            if (changed != 1) throw new InvalidOperationException($"repair_in_place_precondition_failed:{record.Neo4jElementId}");
        }
    }

    private static async Task CreateRelationshipsAsync(IAsyncQueryRunner tx, MalformedTagRepairPlan plan)
    {
        foreach (var operation in plan.RelationshipOperations.Where(o => o.Action == RelationshipRepairActions.Create))
        {
            if (!MalformedTagRepairPlanner.IsSafeRelationshipType(operation.Type))
                throw new InvalidOperationException("unsafe_relationship_type_in_plan");
            var cypher = $@"
MATCH (a), (b) WHERE elementId(a) = $startId AND elementId(b) = $endId
CREATE (a)-[r:`{operation.Type}`]->(b)
SET r = $properties
RETURN count(r) AS created";
            var cursor = await tx.RunAsync(cypher, new
            {
                startId = operation.StartNodeElementId,
                endId = operation.EndNodeElementId,
                properties = ToNeo4jMap(operation.Properties)
            });
            var created = await cursor.SingleAsync(r => r["created"].As<long>());
            if (created != 1) throw new InvalidOperationException($"relationship_create_failed:{operation.SourceRelationshipElementId}");
        }
    }

    private static async Task<int> VerifyPlannedRelationshipsAsync(IAsyncQueryRunner tx, MalformedTagRepairPlan plan)
    {
        var verified = 0;
        foreach (var operation in plan.RelationshipOperations)
        {
            if (!MalformedTagRepairPlanner.IsSafeRelationshipType(operation.Type))
                throw new InvalidOperationException("unsafe_relationship_type_in_plan");
            var cypher = $@"
MATCH (a)-[r:`{operation.Type}`]->(b)
WHERE elementId(a) = $startId AND elementId(b) = $endId AND properties(r) = $properties
RETURN count(r) AS matches";
            var cursor = await tx.RunAsync(cypher, new
            {
                startId = operation.StartNodeElementId,
                endId = operation.EndNodeElementId,
                properties = ToNeo4jMap(operation.Properties)
            });
            var matches = await cursor.SingleAsync(r => r["matches"].As<long>());
            if (matches < 1) throw new InvalidOperationException($"relationship_verification_failed:{operation.SourceRelationshipElementId}");
            verified++;
        }
        return verified;
    }

    private static async Task DeleteMergedNodesAsync(IAsyncQueryRunner tx, MalformedTagRepairPlan plan)
    {
        foreach (var record in plan.Records.Where(r => r.ExpectedMalformedNodeDeletion))
        {
            var cursor = await tx.RunAsync(@"
MATCH (t:Tag) WHERE elementId(t) = $elementId AND t.stableId IS NULL AND t.id IS NULL
DETACH DELETE t
RETURN count(*) AS deleted", new { elementId = record.Neo4jElementId });
            var deleted = await cursor.SingleAsync(r => r["deleted"].As<long>());
            if (deleted != 1) throw new InvalidOperationException($"malformed_node_delete_precondition_failed:{record.Neo4jElementId}");
        }
    }

    private static List<string> VerifyPostconditions(
        MalformedTagRepairPlan plan,
        IReadOnlyList<RepairSqlTag> sqlTags,
        RepairGraphSnapshot after)
    {
        var failures = new List<string>();
        if (after.Relationships.Count != plan.VerificationPlan.ExpectedRelationshipCountAfter) failures.Add("relationship_count");
        if (CountDuplicateStableIds(after) != plan.VerificationPlan.ExpectedDuplicateExplicitStableIdCount) failures.Add("stable_id_uniqueness");
        if (CountSqlCoverage(sqlTags, after) != plan.VerificationPlan.ExpectedExplicitStableIdCoverage) failures.Add("sql_current_tag_coverage");
        if (CountMalformedNodes(after) != plan.VerificationPlan.ExpectedMalformedNodeCountAfter) failures.Add("malformed_node_count");
        if (!VerifyHierarchy(plan, after)) failures.Add("hierarchy_diagnostics");
        foreach (var record in plan.Records.Where(r => r.PlannedAction == MalformedTagRepairActions.RepairInPlace))
        {
            var node = after.Nodes.SingleOrDefault(n => n.ElementId == record.Neo4jElementId);
            if (node == null
                || !string.Equals(ReadString(node.Properties, "stableId"), record.ExpectedPostRepairStableId?.ToString(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(ReadString(node.Properties, "currentVersionId"), record.MatchedSqlTagRowId?.ToString(), StringComparison.OrdinalIgnoreCase)
                || node.Properties.ContainsKey("name") || node.Properties.ContainsKey("status") || node.Properties.ContainsKey("isCurrent"))
                failures.Add($"projection_contract:{record.Neo4jElementId}");
        }
        return failures;
    }

    private static bool VerifyHierarchy(MalformedTagRepairPlan plan, RepairGraphSnapshot snapshot)
    {
        var contain = snapshot.Relationships.Where(r => r.Type == "CONTAIN").ToList();
        return contain.Count == plan.VerificationPlan.ExpectedContainRelationshipCount
               && MalformedTagRepairPlanner.CountCycleEdges(contain) == plan.VerificationPlan.ExpectedContainCycleEdgeCount
               && MalformedTagRepairPlanner.ComputeContainLogicalEdgeHash(contain) == plan.VerificationPlan.ExpectedContainLogicalEdgeHash;
    }

    private static int CountMalformedNodes(RepairGraphSnapshot snapshot)
        => snapshot.Nodes.Count(n => n.Labels.Contains("Tag", StringComparer.Ordinal)
                                     && IsMissing(n.Properties, "stableId") && IsMissing(n.Properties, "id"));

    private static int CountDuplicateStableIds(RepairGraphSnapshot snapshot)
        => snapshot.Nodes.Where(n => n.Labels.Contains("Tag", StringComparer.Ordinal))
            .Select(n => ReadString(n.Properties, "stableId"))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Count(g => g.Count() > 1);

    private static int CountSqlCoverage(IReadOnlyList<RepairSqlTag> sqlTags, RepairGraphSnapshot snapshot)
    {
        var graphIds = snapshot.Nodes.Where(n => n.Labels.Contains("Tag", StringComparer.Ordinal))
            .Select(n => ReadString(n.Properties, "stableId"))
            .Where(v => Guid.TryParse(v, out _))
            .Select(value => Guid.Parse(value!))
            .ToHashSet();
        return sqlTags.Where(t => t.IsCurrent).Select(t => t.StableId).Distinct().Count(graphIds.Contains);
    }

    private static MalformedTagRepairResult FailureResult(string planHash, DateTimeOffset started, string code,
        string message, int planned, int quarantined)
        => new(planHash, started, DateTimeOffset.UtcNow, false, "failed_closed", planned, 0, 0, quarantined,
            0, 0, 0, 0, 0, 0, 0, 0, false, true, new[] { code }, code, message);

    private static string SafeMessage(Exception exception)
        => exception.Message.Length <= 500 ? exception.Message : exception.Message[..500];

    private static IReadOnlyDictionary<string, object?> NormalizeProperties(IDictionary<string, object>? properties)
        => properties == null
            ? new Dictionary<string, object?>()
            : properties.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToDictionary(kv => kv.Key, kv => NormalizeValue(kv.Value), StringComparer.Ordinal);

    private static object? NormalizeValue(object? value)
        => value switch
        {
            null => null,
            string or bool or int or long or double or float => value,
            Guid guid => guid.ToString(),
            DateTime dateTime => dateTime.ToString("O"),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O"),
            System.Collections.IEnumerable values => values.Cast<object?>().Select(NormalizeValue).ToList(),
            _ => value.ToString()
        };

    private static Dictionary<string, object?> ToNeo4jMap(IReadOnlyDictionary<string, object?> properties)
        => properties.ToDictionary(kv => kv.Key, kv => FromJsonElement(kv.Value), StringComparer.Ordinal);

    private static object? FromJsonElement(object? value)
        => value is not JsonElement element ? value : element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.Array => element.EnumerateArray().Select(item => FromJsonElement(item)).ToList(),
            _ => element.ToString()
        };

    private static bool IsMissing(IReadOnlyDictionary<string, object?> properties, string key)
        => !properties.TryGetValue(key, out var value) || value == null || string.IsNullOrWhiteSpace(value.ToString());

    private static string? ReadString(IReadOnlyDictionary<string, object?> properties, string key)
        => properties.TryGetValue(key, out var value) ? value?.ToString() : null;
}

public static class MalformedTagRepairTransactionPolicy
{
    public const bool UsesSingleAtomicNeo4jTransaction = true;

    public static bool CanDeleteMalformedNodes(int expectedRelationshipVerifications, int completedRelationshipVerifications)
        => expectedRelationshipVerifications == completedRelationshipVerifications;
}
