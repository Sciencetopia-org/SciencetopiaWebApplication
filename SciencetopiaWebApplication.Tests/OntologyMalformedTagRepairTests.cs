using Microsoft.Extensions.Configuration;
using Sciencetopia.Services.Ontology.TagRepair;

namespace SciencetopiaWebApplication.Tests;

public class OntologyMalformedTagRepairTests
{
    private static readonly Guid StableId = Guid.Parse("23378f8d-5760-8081-87f7-f26f40151bc5");
    private static readonly Guid RowId = Guid.Parse("12345678-1234-1234-1234-123456789012");

    [Fact]
    public void ValidEmbeddedStableId_MatchesCurrentSql()
    {
        var plan = Plan(nodes: new[] { Malformed("m") });
        var record = Assert.Single(plan.Records);
        Assert.Equal(StableId, record.CanonicalGuid);
        Assert.Equal(RowId, record.MatchedSqlTagRowId);
    }

    [Theory]
    [InlineData("bad 1234")]
    [InlineData("23378f8d5760808187f7f26f40151bc5 trailing")]
    [InlineData("a23378f8d5760808187f7f26f40151bc5")]
    public void MalformedSuffix_IsRejected(string name)
    {
        Assert.Null(MalformedTagRepairPlanner.TryExtractStableId(name));
        Assert.Equal(MalformedTagRepairActions.LeaveQuarantined, Plan(nodes: new[] { Malformed("m", name) }).Records.Single().PlannedAction);
    }

    [Fact]
    public void CanonicalNodeExists_PlansMerge()
    {
        var plan = Plan(nodes: new[] { Malformed("m"), Canonical("c") });
        Assert.Equal(MalformedTagRepairActions.MergeIntoCanonicalNode, plan.Records.Single().PlannedAction);
    }

    [Fact]
    public void CanonicalNodeAbsent_PlansRepairInPlace()
    {
        Assert.Equal(MalformedTagRepairActions.RepairInPlace, Plan(nodes: new[] { Malformed("m") }).Records.Single().PlannedAction);
    }

    [Fact]
    public void DuplicateCanonicalNodes_AreBlocked()
    {
        var record = Plan(nodes: new[] { Malformed("m"), Canonical("c1"), Canonical("c2") }).Records.Single();
        Assert.Contains("multiple_canonical_neo4j_tags_for_recovered_stable_id", record.BlockingReasons);
    }

    [Fact]
    public void MissingSqlMatch_IsBlocked()
    {
        var plan = Plan(sql: Array.Empty<RepairSqlTag>(), nodes: new[] { Malformed("m") });
        Assert.Contains("recovered_stable_id_not_found_in_current_sql_tags", plan.Records.Single().BlockingReasons);
    }

    [Fact]
    public void CanonicalNameUnavailable_IsBlockedWhenProjectionStoresName()
    {
        var contract = new TagProjectionContract("stableId", "currentVersionId", true, "name", new[] { "status", "isCurrent" });
        var plan = Plan(sql: new[] { Sql(name: null) }, nodes: new[] { Malformed("m") }, contract: contract);
        Assert.Contains("canonical_name_unavailable", plan.Records.Single().BlockingReasons);
    }

    [Fact]
    public void IncomingRelationship_IsMigratedWithDirection()
    {
        var relationship = Rel("r", "external", "m", "TAGGED_WITH");
        var operation = Plan(nodes: new[] { Malformed("m"), Canonical("c"), External("external") }, relationships: new[] { relationship })
            .RelationshipOperations.Single();
        Assert.Equal("external", operation.StartNodeElementId);
        Assert.Equal("c", operation.EndNodeElementId);
    }

    [Fact]
    public void OutgoingRelationship_IsMigratedWithDirection()
    {
        var operation = Plan(nodes: new[] { Malformed("m"), Canonical("c"), External("external") },
            relationships: new[] { Rel("r", "m", "external", "TAGGED_WITH") }).RelationshipOperations.Single();
        Assert.Equal("c", operation.StartNodeElementId);
        Assert.Equal("external", operation.EndNodeElementId);
    }

    [Fact]
    public void ExactDuplicateRelationship_IsSkipped()
    {
        var properties = Props(("weight", 1L));
        var plan = Plan(nodes: new[] { Malformed("m"), Canonical("c"), External("external") }, relationships: new[]
        {
            Rel("old", "m", "external", "TAGGED_WITH", properties),
            Rel("existing", "c", "external", "TAGGED_WITH", properties)
        });
        Assert.Equal(RelationshipRepairActions.SkipExactDuplicate, plan.RelationshipOperations.Single().Action);
    }

    [Fact]
    public void SemanticallyDistinctParallelRelationship_IsPreserved()
    {
        var plan = Plan(nodes: new[] { Malformed("m"), Canonical("c"), External("external") }, relationships: new[]
        {
            Rel("r1", "m", "external", "TAGGED_WITH", Props(("source", "a"))),
            Rel("r2", "m", "external", "TAGGED_WITH", Props(("source", "b")))
        });
        Assert.Equal(2, plan.RelationshipOperations.Count(o => o.Action == RelationshipRepairActions.Create));
    }

    [Fact]
    public void RelationshipProperties_ArePreservedInPlan()
    {
        var properties = Props(("weight", 3L), ("source", "legacy"));
        var operation = Plan(nodes: new[] { Malformed("m"), Canonical("c"), External("external") },
            relationships: new[] { Rel("r", "m", "external", "CONTAIN", properties) }).RelationshipOperations.Single();
        Assert.Equal(properties, operation.Properties);
    }

    [Fact]
    public void MergeCreatedSelfLoop_IsBlocked()
    {
        var plan = Plan(nodes: new[] { Malformed("m"), Canonical("c") },
            relationships: new[] { Rel("r", "m", "c", "CONTAIN") });
        Assert.Contains("merge_would_create_self_loop", plan.Records.Single().BlockingReasons);
    }

    [Fact]
    public void MalformedNodeDeletion_RequiresAllRelationshipVerification()
    {
        Assert.False(MalformedTagRepairTransactionPolicy.CanDeleteMalformedNodes(2, 1));
        Assert.True(MalformedTagRepairTransactionPolicy.CanDeleteMalformedNodes(2, 2));
    }

    [Fact]
    public void StalePlanSnapshot_IsDetectedByHashMismatch()
    {
        var original = new RepairGraphSnapshot(new[] { Malformed("m") }, Array.Empty<RepairGraphRelationship>());
        var changed = new RepairGraphSnapshot(new[] { Malformed("m", "changed 23378f8d5760808187f7f26f40151bc5") }, Array.Empty<RepairGraphRelationship>());
        Assert.NotEqual(MalformedTagRepairPlanner.ComputeGraphSnapshotHash(original), MalformedTagRepairPlanner.ComputeGraphSnapshotHash(changed));
    }

    [Fact]
    public void MissingExplicitWriteConfirmation_BlocksApply()
    {
        var plan = Plan(nodes: new[] { Malformed("m") });
        var result = MalformedTagRepairCli.ValidateApplyRequest(new TagRepairApplyRequest("plan.json", plan.PlanHash, false), plan);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("--confirm-neo4j-write", StringComparison.Ordinal));
    }

    [Fact]
    public void PlanMode_IsPureAndPerformsNoWrites()
    {
        var node = Malformed("m");
        var before = node.Properties.ToDictionary(kv => kv.Key, kv => kv.Value);
        _ = Plan(nodes: new[] { node });
        Assert.Equal(before, node.Properties);
    }

    [Fact]
    public void FailedTransaction_UsesAtomicRollbackPolicy()
    {
        Assert.True(MalformedTagRepairTransactionPolicy.UsesSingleAtomicNeo4jTransaction);
        Assert.False(MalformedTagRepairTransactionPolicy.CanDeleteMalformedNodes(1, 0));
    }

    [Fact]
    public void PlanVerifiesRepairedStableIdUniqueness()
    {
        var plan = Plan(nodes: new[] { Malformed("m") });
        Assert.Equal(0, plan.VerificationPlan.ExpectedDuplicateExplicitStableIdCount);
        Assert.Contains(plan.VerificationPlan.Postconditions, p => p.Contains("duplicate explicit", StringComparison.Ordinal));
    }

    [Fact]
    public void RollbackArtifact_ContainsNodeAndCompleteRelationshipData()
    {
        var relationship = Rel("r", "m", "external", "TAGGED_WITH", Props(("weight", 2L)));
        var backup = Plan(nodes: new[] { Malformed("m"), Canonical("c"), External("external") }, relationships: new[] { relationship })
            .Backup.Single();
        Assert.Equal("m", backup.ElementId);
        Assert.Equal("Current", backup.Properties["status"]);
        var backedUpRelationship = Assert.Single(backup.Relationships);
        Assert.Equal((relationship.StartNodeElementId, relationship.EndNodeElementId, relationship.Type, relationship.Properties),
            (backedUpRelationship.StartNodeElementId, backedUpRelationship.EndNodeElementId, backedUpRelationship.Type, backedUpRelationship.Properties));
        Assert.Contains(backup.EndpointNodes, n => n.ElementId == "external" && n.Labels.Contains("KnowledgeNode"));
    }

    [Fact]
    public void PlanHash_RoundTripsAndDetectsModification()
    {
        var plan = Plan(nodes: new[] { Malformed("m") });
        Assert.True(MalformedTagRepairPlanner.HasValidHash(plan));
        Assert.False(MalformedTagRepairPlanner.HasValidHash(plan with { SchemaVersion = "modified" }));
    }

    [Fact]
    public void ConfigurationPreflight_CollectsSqlAndNeo4jErrors()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var result = MalformedTagRepairCli.ValidateConfiguration(configuration);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("ConnectionStrings:DefaultConnection", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("Neo4j:Uri", StringComparison.Ordinal));
    }

    [Fact]
    public void CurrentSqlTagWithoutRowId_IsBlocked()
    {
        var sql = new RepairSqlTag(null, StableId, "Canonical", "Current", true);
        var record = Plan(sql: new[] { sql }, nodes: new[] { Malformed("m") }).Records.Single();
        Assert.Contains("current_sql_tag_row_id_missing", record.BlockingReasons);
    }

    [Fact]
    public void ArtifactWriter_ProducesRequiredPlanBackupSummaryAndVerificationFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tag-repair-test-{Guid.NewGuid():N}");
        try
        {
            var planPath = MalformedTagRepairArtifacts.WritePlan(Plan(nodes: new[] { Malformed("m") }), directory);
            Assert.True(File.Exists(planPath));
            Assert.True(File.Exists(Path.Combine(directory, MalformedTagRepairArtifacts.SummaryFileName)));
            Assert.True(File.Exists(Path.Combine(directory, MalformedTagRepairArtifacts.BackupFileName)));
            Assert.True(File.Exists(Path.Combine(directory, MalformedTagRepairArtifacts.VerificationFileName)));
            Assert.True(MalformedTagRepairPlanner.HasValidHash(MalformedTagRepairArtifacts.ReadPlan(planPath)));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static MalformedTagRepairPlan Plan(
        IReadOnlyList<RepairSqlTag>? sql = null,
        IReadOnlyList<RepairGraphNode>? nodes = null,
        IReadOnlyList<RepairGraphRelationship>? relationships = null,
        TagProjectionContract? contract = null)
        => MalformedTagRepairPlanner.Build(
            sql ?? new[] { Sql() },
            new RepairGraphSnapshot(nodes ?? Array.Empty<RepairGraphNode>(), relationships ?? Array.Empty<RepairGraphRelationship>()),
            contract,
            DateTimeOffset.Parse("2026-07-12T00:00:00Z"));

    private static RepairSqlTag Sql(string? name = "Canonical") => new(RowId, StableId, name, "Current", true);

    private static RepairGraphNode Malformed(string id, string? name = null) => new(id, new[] { "Tag" }, Props(
        ("name", name ?? $"corrupted {StableId:N}"), ("isCurrent", true), ("status", "Current")));

    private static RepairGraphNode Canonical(string id) => new(id, new[] { "Tag" }, Props(
        ("stableId", StableId.ToString()), ("currentVersionId", RowId.ToString())));

    private static RepairGraphNode External(string id) => new(id, new[] { "KnowledgeNode" }, Props(("stableId", Guid.NewGuid().ToString())));

    private static RepairGraphRelationship Rel(string id, string start, string end, string type,
        IReadOnlyDictionary<string, object?>? properties = null)
        => new(id, type, start, end, properties ?? Props());

    private static IReadOnlyDictionary<string, object?> Props(params (string Key, object? Value)[] values)
        => values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
}
