using System.Text.Json;

namespace Sciencetopia.Services.Ontology.TagRepair;

public static class MalformedTagRepairArtifacts
{
    public const string PlanFileName = "malformed_tag_repair_plan.json";
    public const string SummaryFileName = "repair_summary.json";
    public const string BackupFileName = "repair_backup.json";
    public const string VerificationFileName = "repair_verification_plan.json";
    public const string ResultFileName = "malformed_tag_repair_result.json";

    public static string WritePlan(MalformedTagRepairPlan plan, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        Write(Path.Combine(outputDirectory, PlanFileName), plan);
        Write(Path.Combine(outputDirectory, SummaryFileName), plan.Summary);
        Write(Path.Combine(outputDirectory, BackupFileName), plan.Backup);
        Write(Path.Combine(outputDirectory, VerificationFileName), plan.VerificationPlan);
        return Path.Combine(outputDirectory, PlanFileName);
    }

    public static MalformedTagRepairPlan ReadPlan(string path)
    {
        var plan = JsonSerializer.Deserialize<MalformedTagRepairPlan>(File.ReadAllText(path), MalformedTagRepairPlanner.Json);
        return plan ?? throw new InvalidDataException("The repair plan file is empty or invalid JSON.");
    }

    public static string WriteResult(MalformedTagRepairResult result, string planFilePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(planFilePath))
                        ?? throw new InvalidOperationException("The plan file has no parent directory.");
        var path = Path.Combine(directory, ResultFileName);
        Write(path, result);
        return path;
    }

    private static void Write<T>(string path, T value)
        => File.WriteAllText(path, JsonSerializer.Serialize(value, MalformedTagRepairPlanner.Json));
}
