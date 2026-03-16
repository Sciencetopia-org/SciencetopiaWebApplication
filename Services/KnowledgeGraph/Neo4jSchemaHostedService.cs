using Neo4j.Driver;

namespace Sciencetopia.Services.KnowledgeGraph;

public sealed class Neo4jSchemaHostedService : IHostedService
{
    private static readonly string[] Statements =
    {
        "CREATE CONSTRAINT tag_stable_id IF NOT EXISTS FOR (n:Tag) REQUIRE n.stableId IS UNIQUE",
        "CREATE CONSTRAINT knowledge_node_stable_id IF NOT EXISTS FOR (n:KnowledgeNode) REQUIRE n.stableId IS UNIQUE",
        "CREATE CONSTRAINT user_id IF NOT EXISTS FOR (n:User) REQUIRE n.id IS UNIQUE",
        "CREATE CONSTRAINT study_plan_id IF NOT EXISTS FOR (n:StudyPlan) REQUIRE n.id IS UNIQUE",
        "CREATE CONSTRAINT lesson_id IF NOT EXISTS FOR (n:Lesson) REQUIRE n.id IS UNIQUE",
        "CREATE CONSTRAINT resource_id IF NOT EXISTS FOR (n:Resource) REQUIRE n.id IS UNIQUE",
        "CREATE RANGE INDEX tag_level_name IF NOT EXISTS FOR (n:TagLevel) ON (n.name)",
        "CREATE RANGE INDEX knowledge_node_status IF NOT EXISTS FOR (n:KnowledgeNode) ON (n.status)",
        "CREATE RANGE INDEX plan_version_study_plan_id IF NOT EXISTS FOR (n:PlanVersion) ON (n.studyPlanId)"
    };

    private readonly IDriver _driver;
    private readonly ILogger<Neo4jSchemaHostedService> _logger;

    public Neo4jSchemaHostedService(IDriver driver, ILogger<Neo4jSchemaHostedService> logger)
    {
        _driver = driver;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var session = _driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Write));
            foreach (var statement in Statements)
            {
                var cursor = await session.RunAsync(statement);
                await cursor.ConsumeAsync();
            }

            _logger.LogInformation("Neo4j schema ensured for knowledge graph hot paths.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Neo4j schema initialization skipped. Knowledge graph queries may remain slow without stableId/name indexes.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
