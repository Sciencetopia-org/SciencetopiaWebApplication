using Microsoft.Extensions.Configuration;

namespace Sciencetopia.Services.Ontology.Phase3;

public sealed record Phase3CliPreflightResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static Phase3CliPreflightResult Valid { get; } = new(true, Array.Empty<string>());
}

public static class Phase3CliPreflight
{
    private static readonly HashSet<string> SupportedNeo4jSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "bolt",
        "bolt+s",
        "bolt+ssc",
        "neo4j",
        "neo4j+s",
        "neo4j+ssc"
    };

    public static Phase3CliPreflightResult ValidateNeo4jConfiguration(IConfiguration configuration)
    {
        var errors = new List<string>();
        var uri = configuration["Neo4j:Uri"];
        var user = configuration["Neo4j:User"];
        var password = configuration["Neo4j:Password"];

        if (string.IsNullOrWhiteSpace(uri))
        {
            errors.Add("Neo4j:Uri is missing or empty.");
        }
        else if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
        {
            errors.Add("Neo4j:Uri must be an absolute URI, for example bolt://localhost:7687.");
        }
        else if (!SupportedNeo4jSchemes.Contains(parsedUri.Scheme))
        {
            errors.Add("Neo4j:Uri must use a Neo4j driver scheme: bolt, bolt+s, bolt+ssc, neo4j, neo4j+s, or neo4j+ssc.");
        }

        if (string.IsNullOrWhiteSpace(user))
        {
            errors.Add("Neo4j:User is missing or empty.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            errors.Add("Neo4j:Password is missing or empty.");
        }

        return errors.Count == 0
            ? Phase3CliPreflightResult.Valid
            : new Phase3CliPreflightResult(false, errors);
    }
}
