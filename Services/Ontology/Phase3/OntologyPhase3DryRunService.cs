using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Sciencetopia.Data;

namespace Sciencetopia.Services.Ontology.Phase3;

// Phase 3 dry-run orchestrator. READ-ONLY: reads current SQL Tags (EF) and Neo4j
// :Tag/:TagLevel/:CONTAIN via MATCH/RETURN only, classifies deterministically, and writes
// local JSON artifacts. It performs NO writes to SQL or Neo4j and creates NO canonical ontology.
//
// Invocation: this is NOT registered in DI and exposes NO API endpoint (Phase 3 scope). Run it
// manually from a throwaway scope (or a future admin-only command) with a live read-only DB +
// Neo4j connection, e.g.:
//     var svc = new OntologyPhase3DryRunService(db, driver);
//     await svc.RunAsync("artifacts");
// See docs/ontology_phase3_dry_run.md.
public sealed class OntologyPhase3DryRunService
{
    private readonly ApplicationDbContext _db;
    private readonly IDriver _driver;

    public OntologyPhase3DryRunService(ApplicationDbContext db, IDriver driver)
    {
        _db = db;
        _driver = driver;
    }

    /// <summary>Reads SQL + Neo4j (read-only) and returns the collected inputs.</summary>
    public async Task<Phase3Inputs> CollectAsync(CancellationToken ct = default)
    {
        var sqlTags = await CollectSqlTagsAsync(ct);
        var sqlTagVersions = await CollectSqlTagVersionsAsync(ct);
        var neoTags = await CollectNeo4jTagsAsync();
        var tagLevels = await CollectTagLevelsAsync();
        var containEdges = await CollectContainEdgesAsync();
        return new Phase3Inputs(sqlTags, neoTags, tagLevels, containEdges, sqlTagVersions);
    }

    /// <summary>Collect -> classify -> write artifacts under
    /// <paramref name="artifactsRoot"/>/ontology_phase3/&lt;utc-timestamp&gt;/. Returns (summary, dir).</summary>
    public async Task<(Phase3Summary Summary, string OutputDir)> RunAsync(string artifactsRoot, CancellationToken ct = default)
    {
        var inputs = await CollectAsync(ct);
        var result = Phase3DryRunEngine.Run(inputs);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        var dir = System.IO.Path.Combine(artifactsRoot, "ontology_phase3", stamp);
        Phase3ArtifactWriter.Write(result, dir);
        return (result.Summary, dir);
    }

    // ---- read-only collectors ----

    private async Task<IReadOnlyList<LegacyTagInput>> CollectSqlTagsAsync(CancellationToken ct)
    {
        var rows = await _db.Tags.AsNoTracking()
            .Where(t => t.IsCurrent)
            .Select(t => new { t.Id, t.StableId, t.Name, t.Status, t.IsCurrent, HasL10n = t.DefaultL10nSetId != null })
            .ToListAsync(ct);

        return rows.Select(r => new LegacyTagInput(r.Id, r.StableId, r.Name, r.Status, r.IsCurrent, r.HasL10n)).ToList();
    }

    private async Task<IReadOnlyList<SqlTagIdentityInput>> CollectSqlTagVersionsAsync(CancellationToken ct)
    {
        var rows = await _db.Tags.AsNoTracking()
            .Select(t => new { t.Id, t.StableId, t.Name, t.Status, t.IsCurrent, HasL10n = t.DefaultL10nSetId != null })
            .ToListAsync(ct);

        return rows.Select(r => new SqlTagIdentityInput(r.Id, r.StableId, r.Name, r.Status, r.IsCurrent, r.HasL10n)).ToList();
    }

    private async Task<IReadOnlyList<Neo4jTagInput>> CollectNeo4jTagsAsync()
    {
        const string cypher = @"
MATCH (t:Tag)
RETURN elementId(t) AS elementId, labels(t) AS labels, properties(t) AS props,
  t.stableId AS stableId, t.id AS id, t.name AS name,
  size([(t)-[:TAGGED_WITH]->(:KnowledgeNode) | 1]) > 0 AS linkedNode,
  size([(t)-[:TAGGED_WITH]->(:StudyGroup) | 1]) > 0 AS linkedGroup,
  (size([(t)-[:CONTAIN]->(:Tag) | 1]) > 0 OR size([(:Tag)-[:CONTAIN]->(t) | 1]) > 0) AS inContain";

        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher);
            return (IReadOnlyList<Neo4jTagInput>)await cursor.ToListAsync(r => new Neo4jTagInput(
                r["stableId"].As<string?>(),
                r["id"].As<string?>(),
                r["linkedNode"].As<bool>(),
                r["linkedGroup"].As<bool>(),
                r["inContain"].As<bool>(),
                r["elementId"].As<string?>(),
                r["name"].As<string?>(),
                r["labels"].As<List<string>>() ?? new List<string>(),
                NormalizeProperties(r["props"].As<Dictionary<string, object>>())));
        });
    }

    private async Task<IReadOnlyList<TagLevelInput>> CollectTagLevelsAsync()
    {
        const string cypher = @"
MATCH (l:TagLevel)-[:TAGGED_WITH]->(n:KnowledgeNode)
RETURN n.stableId AS nodeStableId, l.name AS level";

        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher);
            return (IReadOnlyList<TagLevelInput>)(await cursor.ToListAsync(r => new TagLevelInput(
                r["nodeStableId"].As<string>() ?? string.Empty,
                r["level"].As<string>() ?? string.Empty)))
                .Where(x => x.NodeStableId.Length > 0 && x.Level.Length > 0).ToList();
        });
    }

    private async Task<IReadOnlyList<ContainEdgeInput>> CollectContainEdgesAsync()
    {
        const string cypher = @"
MATCH (p:Tag)-[r:CONTAIN]->(c:Tag)
RETURN p.stableId AS parentStableId, c.stableId AS childStableId,
  elementId(r) AS relationshipElementId,
  labels(p) AS parentLabels, labels(c) AS childLabels,
  properties(p) AS parentProps, properties(c) AS childProps, properties(r) AS relationshipProps";

        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(cypher);
            var rows = await cursor.ToListAsync(r => new ContainEdgeInput(
                r["parentStableId"].As<string>() ?? string.Empty,
                r["childStableId"].As<string>() ?? string.Empty,
                r["relationshipElementId"].As<string?>(),
                r["parentLabels"].As<List<string>>() ?? new List<string>(),
                r["childLabels"].As<List<string>>() ?? new List<string>(),
                NormalizeProperties(r["parentProps"].As<Dictionary<string, object>>()),
                NormalizeProperties(r["childProps"].As<Dictionary<string, object>>()),
                NormalizeProperties(r["relationshipProps"].As<Dictionary<string, object>>())));
            return (IReadOnlyList<ContainEdgeInput>)rows
                .Where(x => x.ParentStableId.Length > 0 && x.ChildStableId.Length > 0)
                .ToList();
        });
    }

    private static IReadOnlyDictionary<string, object?> NormalizeProperties(IDictionary<string, object>? properties)
    {
        if (properties == null) return new Dictionary<string, object?>();
        return properties
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => NormalizePropertyValue(kv.Value));
    }

    private static object? NormalizePropertyValue(object? value)
        => value switch
        {
            null => null,
            string s => s,
            bool b => b,
            int i => i,
            long l => l,
            double d => d,
            float f => f,
            Guid g => g.ToString(),
            DateTime dt => dt.ToString("O"),
            DateTimeOffset dto => dto.ToString("O"),
            _ => value.ToString()
        };

}
