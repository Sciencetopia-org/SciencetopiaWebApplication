using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Sciencetopia.Services.Ontology.Phase3;

// Serializes a dry-run result to the Phase 3 artifact files. Writes ONLY to the supplied
// local directory (caller uses artifacts/ontology_phase3/<timestamp>/, which is .gitignored).
// Never writes to any database.
public static class Phase3ArtifactWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        // Keep non-ASCII (e.g. Chinese tag names) readable in the artifacts.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Returns filename -> JSON content (no filesystem I/O). Useful for shape tests.</summary>
    public static IReadOnlyDictionary<string, string> Serialize(Phase3DryRunResult r) => new Dictionary<string, string>
    {
        ["legacy_tag_inventory.json"] = JsonSerializer.Serialize(r.LegacyTagInventory, Json),
        ["neo4j_tag_inventory.json"] = JsonSerializer.Serialize(r.Neo4jTagReconciliationInventory, Json),
        ["legacy_tag_quarantine.json"] = JsonSerializer.Serialize(r.Quarantine, Json),
        ["neo4j_hierarchy_extraction.json"] = JsonSerializer.Serialize(r.Hierarchy, Json),
        ["neo4j_hierarchy_diagnostics.json"] = JsonSerializer.Serialize(r.HierarchyDiagnostics, Json),
        ["concept_candidate_proposals.json"] = JsonSerializer.Serialize(r.ConceptCandidates, Json),
        ["tag_value_candidate_proposals.json"] = JsonSerializer.Serialize(r.TagValueCandidates, Json),
        ["concept_placement_candidate_proposals.json"] = JsonSerializer.Serialize(r.Hierarchy.PlacementCandidates, Json),
        ["concept_relation_candidate_proposals.json"] = JsonSerializer.Serialize(r.Hierarchy.RelationCandidates, Json),
        ["phase3_summary.json"] = JsonSerializer.Serialize(r.Summary, Json),
    };

    /// <summary>Writes all artifacts into <paramref name="outputDir"/> (created if missing).</summary>
    public static IReadOnlyList<string> Write(Phase3DryRunResult r, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var written = new List<string>();
        foreach (var (name, json) in Serialize(r))
        {
            var path = Path.Combine(outputDir, name);
            File.WriteAllText(path, json);
            written.Add(path);
        }
        return written;
    }
}
