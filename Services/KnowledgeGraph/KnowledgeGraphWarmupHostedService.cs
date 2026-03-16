namespace Sciencetopia.Services.KnowledgeGraph;

public sealed class KnowledgeGraphWarmupHostedService : BackgroundService
{
    private static readonly string[] DefaultZoomLevels = ["Field", "Subject", "Discipline"];
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KnowledgeGraphWarmupHostedService> _logger;

    public KnowledgeGraphWarmupHostedService(
        IServiceProvider serviceProvider,
        ILogger<KnowledgeGraphWarmupHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var graphService = scope.ServiceProvider.GetRequiredService<KnowledgeGraphService>();

            foreach (var lang in new[] { "zh", "en" })
            {
                stoppingToken.ThrowIfCancellationRequested();
                await graphService.GetKnowledgeGraphInViewAsync(
                    "MainTag",
                    "network",
                    DefaultZoomLevels,
                    string.Empty,
                    lang,
                    includePending: false);
            }

            _logger.LogInformation("Knowledge graph warmup completed for default network views.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Knowledge graph warmup skipped due to exception.");
        }
    }
}
