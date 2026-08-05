using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sciencetopia.Services.Ontology.TagRepair;

public static class MalformedTagRepairPlanner
{
    private static readonly Regex EmbeddedStableIdSuffix = new(
        @"(?i)(?<![0-9a-f])([0-9a-f]{32})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SafeRelationshipType = new(
        @"^[A-Z][A-Z0-9_]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static (string HexSuffix, Guid StableId)? TryExtractStableId(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var match = EmbeddedStableIdSuffix.Match(name.TrimEnd());
        if (!match.Success || !Guid.TryParseExact(match.Groups[1].Value, "N", out var stableId)) return null;
        return (match.Groups[1].Value.ToLowerInvariant(), stableId);
    }

    public static MalformedTagRepairPlan Build(
        IReadOnlyList<RepairSqlTag> sqlTags,
        RepairGraphSnapshot snapshot,
        TagProjectionContract? contract = null,
        DateTimeOffset? createdAtUtc = null)
    {
        contract ??= TagProjectionContract.Current;
        var nodes = snapshot.Nodes.ToDictionary(n => n.ElementId, StringComparer.Ordinal);
        var candidates = snapshot.Nodes
            .Where(n => n.Labels.Contains("Tag", StringComparer.Ordinal)
                        && IsMissing(n.Properties, "stableId")
                        && IsMissing(n.Properties, "id"))
            .OrderBy(n => n.ElementId, StringComparer.Ordinal)
            .ToList();
        var sqlByStableId = sqlTags
            .Where(t => t.IsCurrent)
            .GroupBy(t => t.StableId)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.RowId).ToList());
        var canonicalByStableId = snapshot.Nodes
            .Where(n => n.Labels.Contains("Tag", StringComparer.Ordinal))
            .Select(n => (Node: n, StableId: ReadGuid(n.Properties, contract.StableIdProperty)))
            .Where(x => x.StableId.HasValue)
            .GroupBy(x => x.StableId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Node).OrderBy(n => n.ElementId, StringComparer.Ordinal).ToList());
        var drafts = new Dictionary<string, Draft>(StringComparer.Ordinal);
        foreach (var node in candidates)
        {
            var parsed = TryExtractStableId(ReadString(node.Properties, "name"));
            var reasons = new List<string>();
            if (parsed == null)
            {
                reasons.Add("missing_or_invalid_terminal_32_hex_stable_id");
                drafts[node.ElementId] = new Draft(node, null, null, null,
                    MalformedTagRepairActions.LeaveQuarantined, reasons);
                continue;
            }

            sqlByStableId.TryGetValue(parsed.Value.StableId, out var sqlMatches);
            if (sqlMatches == null || sqlMatches.Count == 0)
            {
                reasons.Add("recovered_stable_id_not_found_in_current_sql_tags");
                drafts[node.ElementId] = new Draft(node, parsed, null, null,
                    MalformedTagRepairActions.LeaveQuarantined, reasons);
            }
            else if (sqlMatches.Count > 1)
            {
                reasons.Add("multiple_current_sql_tags_for_recovered_stable_id");
                drafts[node.ElementId] = new Draft(node, parsed, null, null,
                    MalformedTagRepairActions.LeaveQuarantined, reasons);
            }
            else
            {
                drafts[node.ElementId] = new Draft(node, parsed, sqlMatches[0], null,
                    MalformedTagRepairActions.LeaveQuarantined, reasons);
            }
        }

        var malformedMultiplicity = drafts.Values
            .Where(d => d.Sql != null)
            .GroupBy(d => d.Parsed!.Value.StableId)
            .ToDictionary(g => g.Key, g => g.Count());
        foreach (var entry in drafts.Where(e => e.Value.Sql != null).ToList())
        {
            var draft = entry.Value;
            if (!draft.Sql!.RowId.HasValue)
            {
                draft.Block("current_sql_tag_row_id_missing");
                continue;
            }

            var stableId = draft.Parsed!.Value.StableId;
            canonicalByStableId.TryGetValue(stableId, out var canonicalMatches);
            if (canonicalMatches is { Count: > 1 })
            {
                draft.Block("multiple_canonical_neo4j_tags_for_recovered_stable_id");
            }
            else if (canonicalMatches is { Count: 1 })
            {
                drafts[entry.Key] = new Draft(draft.Node, draft.Parsed, draft.Sql, canonicalMatches[0],
                    MalformedTagRepairActions.MergeIntoCanonicalNode, draft.Reasons);
            }
            else if (malformedMultiplicity[stableId] > 1)
            {
                draft.Block("multiple_malformed_nodes_without_canonical_node_for_stable_id");
            }
            else if (contract.StoresName && string.IsNullOrWhiteSpace(draft.Sql.Name))
            {
                draft.Block("canonical_name_unavailable");
            }
            else
            {
                drafts[entry.Key] = new Draft(draft.Node, draft.Parsed, draft.Sql, null,
                    MalformedTagRepairActions.RepairInPlace, draft.Reasons);
            }
        }

        BlockUnsafeRelationshipTypesAndSelfLoops(snapshot.Relationships, drafts);
        var operations = BuildRelationshipOperations(snapshot.Relationships, drafts);
        var records = drafts.Values
            .OrderBy(d => d.Node.ElementId, StringComparer.Ordinal)
            .Select(d => BuildRecord(d, snapshot.Relationships, operations))
            .ToList();
        var backup = candidates.Select(node =>
        {
            var attached = snapshot.Relationships.Where(r => Touches(r, node.ElementId))
                .OrderBy(r => r.ElementId, StringComparer.Ordinal).ToList();
            var endpointIds = attached.SelectMany(r => new[] { r.StartNodeElementId, r.EndNodeElementId })
                .Distinct(StringComparer.Ordinal);
            return new RepairNodeBackup(
                node.ElementId,
                node.Labels,
                node.Properties,
                attached,
                endpointIds.Where(nodes.ContainsKey).Select(id => nodes[id]).OrderBy(n => n.ElementId, StringComparer.Ordinal).ToList());
        })
            .ToList();
        var blocking = records
            .Where(r => r.BlockingReasons.Count > 0)
            .SelectMany(r => r.BlockingReasons.Select(reason => $"{r.Neo4jElementId}:{reason}"))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        var mergeCount = records.Count(r => r.PlannedAction == MalformedTagRepairActions.MergeIntoCanonicalNode);
        var repairCount = records.Count(r => r.PlannedAction == MalformedTagRepairActions.RepairInPlace);
        var deleteCount = records.Count(r => r.ExpectedMalformedNodeDeletion);
        var beforeRelationships = snapshot.Relationships.Count;
        var relationshipsRemovedByMerge = snapshot.Relationships.Count(r =>
            drafts.TryGetValue(r.StartNodeElementId, out var start) && start.Action == MalformedTagRepairActions.MergeIntoCanonicalNode
            || drafts.TryGetValue(r.EndNodeElementId, out var end) && end.Action == MalformedTagRepairActions.MergeIntoCanonicalNode);
        var expectedAfterRelationships = beforeRelationships
                                         - relationshipsRemovedByMerge
                                         + operations.Count(o => o.Action == RelationshipRepairActions.Create);
        var expectedRelationships = BuildExpectedRelationships(snapshot.Relationships, drafts, operations);
        var expectedContain = expectedRelationships.Where(r => r.Type == "CONTAIN").ToList();
        var sqlCurrentStableIds = sqlTags.Where(t => t.IsCurrent).Select(t => t.StableId).Distinct().ToHashSet();
        var expectedCoverage = sqlCurrentStableIds.Count(id =>
            canonicalByStableId.ContainsKey(id)
            || records.Any(r => r.ExpectedPostRepairStableId == id && r.BlockingReasons.Count == 0));
        if (expectedCoverage != sqlCurrentStableIds.Count)
            blocking.Add($"global:sql_current_tag_projection_coverage_not_complete:{expectedCoverage}/{sqlCurrentStableIds.Count}");
        var summary = new MalformedTagRepairSummary(
            records.Count,
            mergeCount,
            repairCount,
            records.Count(r => r.PlannedAction == MalformedTagRepairActions.LeaveQuarantined),
            blocking.Count,
            operations.Count(o => o.Action == RelationshipRepairActions.Create),
            operations.Count(o => o.Action == RelationshipRepairActions.SkipExactDuplicate),
            deleteCount,
            "Tag { stableId, currentVersionId }; no name/status/isCurrent projection properties; unique stableId constraint");
        var verification = new RepairVerificationPlan(
            sqlCurrentStableIds.Count,
            candidates.Count,
            candidates.Count - mergeCount - repairCount,
            beforeRelationships,
            expectedAfterRelationships,
            expectedCoverage,
            0,
            expectedContain.Count,
            CountCycleEdges(expectedContain),
            ComputeContainLogicalEdgeHash(expectedContain),
            ComputeSqlSnapshotHash(sqlTags),
            ComputeGraphSnapshotHash(snapshot),
            new[]
            {
                "plan preconditions still match SQL and Neo4j snapshots",
                "all migrated relationship types, directions, and properties exist before deletion",
                "no affected external node loses an original relationship identity",
                "no duplicate explicit Tag.stableId values remain",
                "all repaired stableIds match current SQL Tags",
                "projection-owned malformed properties follow the current projection contract",
                "CONTAIN logical edge diagnostics remain consistent"
            });
        var unhashed = new MalformedTagRepairPlan(
            "ontology-malformed-tag-repair/v1",
            createdAtUtc ?? DateTimeOffset.UtcNow,
            string.Empty,
            contract,
            records,
            operations,
            backup,
            summary,
            verification,
            blocking);
        return unhashed with { PlanHash = ComputePlanHash(unhashed) };
    }

    public static string ComputePlanHash(MalformedTagRepairPlan plan)
    {
        var json = JsonSerializer.Serialize(plan with { PlanHash = string.Empty }, Json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public static bool HasValidHash(MalformedTagRepairPlan plan)
        => string.Equals(plan.PlanHash, ComputePlanHash(plan), StringComparison.OrdinalIgnoreCase);

    public static string ComputeSqlSnapshotHash(IReadOnlyList<RepairSqlTag> tags)
        => Hash(JsonSerializer.Serialize(tags.OrderBy(t => t.RowId), Json));

    public static string ComputeGraphSnapshotHash(RepairGraphSnapshot snapshot)
    {
        var normalized = new RepairGraphSnapshot(
            snapshot.Nodes.OrderBy(n => n.ElementId, StringComparer.Ordinal).ToList(),
            snapshot.Relationships.OrderBy(r => r.ElementId, StringComparer.Ordinal).ToList());
        return Hash(JsonSerializer.Serialize(normalized, Json));
    }

    public static bool IsSafeRelationshipType(string type) => SafeRelationshipType.IsMatch(type);

    public static string ComputeContainLogicalEdgeHash(IEnumerable<RepairGraphRelationship> relationships)
    {
        var edges = relationships
            .Where(r => r.Type == "CONTAIN")
            .Select(r => $"{r.StartNodeElementId}>{r.EndNodeElementId}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal);
        return Hash(string.Join("\n", edges));
    }

    public static int CountCycleEdges(IEnumerable<RepairGraphRelationship> relationships)
    {
        var edges = relationships.Where(r => r.Type == "CONTAIN")
            .Select(r => (From: r.StartNodeElementId, To: r.EndNodeElementId))
            .Distinct()
            .ToList();
        var adjacency = edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.Select(e => e.To).ToList(), StringComparer.Ordinal);
        bool Reaches(string current, string target, HashSet<string> visited)
        {
            if (!visited.Add(current) || !adjacency.TryGetValue(current, out var next)) return false;
            return next.Any(n => n == target || Reaches(n, target, visited));
        }
        return edges.Count(e => e.From == e.To || Reaches(e.To, e.From, new HashSet<string>(StringComparer.Ordinal)));
    }

    private static void BlockUnsafeRelationshipTypesAndSelfLoops(
        IReadOnlyList<RepairGraphRelationship> relationships,
        IReadOnlyDictionary<string, Draft> drafts)
    {
        foreach (var relationship in relationships)
        {
            var affected = new[] { relationship.StartNodeElementId, relationship.EndNodeElementId }
                .Where(drafts.ContainsKey)
                .Select(id => drafts[id])
                .Distinct()
                .ToList();
            if (affected.Count == 0) continue;

            if (!IsSafeRelationshipType(relationship.Type))
            {
                foreach (var draft in affected.Where(d => d.Action == MalformedTagRepairActions.MergeIntoCanonicalNode))
                    draft.Block("unsupported_relationship_type");
                continue;
            }

            var mappedStart = MapEndpoint(relationship.StartNodeElementId, drafts);
            var mappedEnd = MapEndpoint(relationship.EndNodeElementId, drafts);
            if (mappedStart == mappedEnd && relationship.StartNodeElementId != relationship.EndNodeElementId)
            {
                foreach (var draft in affected.Where(d => d.Action == MalformedTagRepairActions.MergeIntoCanonicalNode))
                    draft.Block("merge_would_create_self_loop");
            }
        }
    }

    private static IReadOnlyList<PlannedRelationshipOperation> BuildRelationshipOperations(
        IReadOnlyList<RepairGraphRelationship> relationships,
        IReadOnlyDictionary<string, Draft> drafts)
    {
        var existing = relationships.Select(RelationshipIdentity).ToHashSet(StringComparer.Ordinal);
        var planned = new HashSet<string>(StringComparer.Ordinal);
        var operations = new List<PlannedRelationshipOperation>();
        foreach (var relationship in relationships.OrderBy(r => r.ElementId, StringComparer.Ordinal))
        {
            var startMerged = drafts.TryGetValue(relationship.StartNodeElementId, out var start)
                              && start.Action == MalformedTagRepairActions.MergeIntoCanonicalNode;
            var endMerged = drafts.TryGetValue(relationship.EndNodeElementId, out var end)
                            && end.Action == MalformedTagRepairActions.MergeIntoCanonicalNode;
            if (!startMerged && !endMerged) continue;

            var mappedStart = MapEndpoint(relationship.StartNodeElementId, drafts);
            var mappedEnd = MapEndpoint(relationship.EndNodeElementId, drafts);
            var identity = RelationshipIdentity(mappedStart, mappedEnd, relationship.Type, relationship.Properties);
            var isDuplicate = existing.Contains(identity) || !planned.Add(identity);
            operations.Add(new PlannedRelationshipOperation(
                relationship.ElementId,
                mappedStart,
                mappedEnd,
                relationship.Type,
                relationship.Properties,
                isDuplicate ? RelationshipRepairActions.SkipExactDuplicate : RelationshipRepairActions.Create,
                Hash(identity)));
        }
        return operations;
    }

    private static IReadOnlyList<RepairGraphRelationship> BuildExpectedRelationships(
        IReadOnlyList<RepairGraphRelationship> relationships,
        IReadOnlyDictionary<string, Draft> drafts,
        IReadOnlyList<PlannedRelationshipOperation> operations)
    {
        var expected = relationships
            .Where(r => !(drafts.TryGetValue(r.StartNodeElementId, out var start) && start.Action == MalformedTagRepairActions.MergeIntoCanonicalNode)
                        && !(drafts.TryGetValue(r.EndNodeElementId, out var end) && end.Action == MalformedTagRepairActions.MergeIntoCanonicalNode))
            .ToList();
        expected.AddRange(operations
            .Where(o => o.Action == RelationshipRepairActions.Create)
            .Select((o, i) => new RepairGraphRelationship(
                $"planned:{i}", o.Type, o.StartNodeElementId, o.EndNodeElementId, o.Properties)));
        return expected;
    }

    private static MalformedTagRepairRecord BuildRecord(
        Draft draft,
        IReadOnlyList<RepairGraphRelationship> relationships,
        IReadOnlyList<PlannedRelationshipOperation> operations)
    {
        var attached = relationships.Where(r => Touches(r, draft.Node.ElementId)).ToList();
        var attachedIds = attached.Select(r => r.ElementId).ToHashSet(StringComparer.Ordinal);
        return new MalformedTagRepairRecord(
            draft.Node.ElementId,
            ReadString(draft.Node.Properties, "name"),
            draft.Parsed?.HexSuffix,
            draft.Parsed?.StableId,
            draft.Sql?.RowId,
            draft.Sql?.StableId,
            draft.Sql?.IsCurrent,
            draft.Sql?.Name,
            draft.Canonical?.ElementId,
            attached.Count(r => r.EndNodeElementId == draft.Node.ElementId),
            attached.Count(r => r.StartNodeElementId == draft.Node.ElementId),
            attached.GroupBy(r => r.Type, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
            draft.Action,
            draft.Reasons,
            operations.Count(o => attachedIds.Contains(o.SourceRelationshipElementId) && o.Action == RelationshipRepairActions.Create),
            operations.Count(o => attachedIds.Contains(o.SourceRelationshipElementId) && o.Action == RelationshipRepairActions.SkipExactDuplicate),
            draft.Action == MalformedTagRepairActions.MergeIntoCanonicalNode,
            draft.Action == MalformedTagRepairActions.MergeIntoCanonicalNode ? draft.Canonical?.ElementId : draft.Node.ElementId,
            draft.Action == MalformedTagRepairActions.LeaveQuarantined ? null : draft.Parsed?.StableId);
    }

    private static string MapEndpoint(string elementId, IReadOnlyDictionary<string, Draft> drafts)
        => drafts.TryGetValue(elementId, out var draft)
           && draft.Action == MalformedTagRepairActions.MergeIntoCanonicalNode
            ? draft.Canonical!.ElementId
            : elementId;

    private static string RelationshipIdentity(RepairGraphRelationship relationship)
        => RelationshipIdentity(relationship.StartNodeElementId, relationship.EndNodeElementId, relationship.Type, relationship.Properties);

    private static string RelationshipIdentity(string start, string end, string type, IReadOnlyDictionary<string, object?> properties)
        => $"{start}\n{type}\n{end}\n{JsonSerializer.Serialize(properties.OrderBy(kv => kv.Key, StringComparer.Ordinal), Json)}";

    private static bool Touches(RepairGraphRelationship relationship, string elementId)
        => relationship.StartNodeElementId == elementId || relationship.EndNodeElementId == elementId;

    private static bool IsMissing(IReadOnlyDictionary<string, object?> properties, string key)
        => !properties.TryGetValue(key, out var value) || value == null || string.IsNullOrWhiteSpace(value.ToString());

    private static string? ReadString(IReadOnlyDictionary<string, object?> properties, string key)
        => properties.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static Guid? ReadGuid(IReadOnlyDictionary<string, object?> properties, string key)
        => Guid.TryParse(ReadString(properties, key), out var value) ? value : null;

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class Draft
    {
        public Draft(RepairGraphNode node, (string HexSuffix, Guid StableId)? parsed, RepairSqlTag? sql,
            RepairGraphNode? canonical, string action, List<string> reasons)
        {
            Node = node;
            Parsed = parsed;
            Sql = sql;
            Canonical = canonical;
            Action = action;
            Reasons = reasons;
        }

        public RepairGraphNode Node { get; }
        public (string HexSuffix, Guid StableId)? Parsed { get; }
        public RepairSqlTag? Sql { get; }
        public RepairGraphNode? Canonical { get; }
        public string Action { get; private set; }
        public List<string> Reasons { get; }

        public void Block(string reason)
        {
            if (!Reasons.Contains(reason, StringComparer.Ordinal)) Reasons.Add(reason);
            Action = MalformedTagRepairActions.LeaveQuarantined;
        }
    }
}
