using Microsoft.Extensions.Configuration;
using Sciencetopia.Services.Ontology.Phase3;
using Xunit;

namespace SciencetopiaWebApplication.Tests;

public class OntologyPhase3CliPreflightTests
{
    private const string ValidNeo4jUri = "bolt://localhost:7687";

    private static IConfiguration Config(params (string Key, string? Value)[] values)
    {
        var data = values.ToDictionary(x => x.Key, x => x.Value);
        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    private static (string Key, string? Value) NeoUri(string? value) => ("Neo4j:Uri", value);
    private static (string Key, string? Value) NeoUser(string? value) => ("Neo4j:User", value);
    private static (string Key, string? Value) NeoPassword(string? value) => ("Neo4j:Password", value);

    [Fact]
    public void MissingNeo4jConfig_FailsClosed_WithAllRequiredKeys()
    {
        var result = Phase3CliPreflight.ValidateNeo4jConfiguration(Config());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Neo4j:Uri"));
        Assert.Contains(result.Errors, e => e.Contains("Neo4j:User"));
        Assert.Contains(result.Errors, e => e.Contains("Neo4j:Password"));
    }

    [Fact]
    public void EmptyOrRelativeUri_FailsClosed()
    {
        var empty = Phase3CliPreflight.ValidateNeo4jConfiguration(Config(
            ("Neo4j:Uri", " "),
            ("Neo4j:User", "neo4j"),
            ("Neo4j:Password", "secret")));
        Assert.False(empty.IsValid);
        Assert.Contains(empty.Errors, e => e.Contains("Neo4j:Uri"));

        var relative = Phase3CliPreflight.ValidateNeo4jConfiguration(Config(
            ("Neo4j:Uri", "localhost:7687"),
            ("Neo4j:User", "neo4j"),
            ("Neo4j:Password", "secret")));
        Assert.False(relative.IsValid);
        Assert.Contains(relative.Errors, e => e.Contains("Neo4j driver scheme"));
    }

    [Fact]
    public void CompleteNeo4jConfig_Passes()
    {
        var result = Phase3CliPreflight.ValidateNeo4jConfiguration(Config(
            NeoUri(ValidNeo4jUri),
            NeoUser("neo4j"),
            NeoPassword("secret")));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}
