public class DailySummaryHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private Timer? _timer = null;

    public DailySummaryHostedService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(GenerateDailySummary, null, TimeSpan.Zero, TimeSpan.FromDays(1));
        return Task.CompletedTask;
    }

    private async void GenerateDailySummary(object? state)
    {
        try
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var summaryService = scope.ServiceProvider.GetRequiredService<DailySummaryService>();
                await summaryService.GenerateDailySummary(DateTime.UtcNow);
            }
        }
        catch (Exception ex)
        {
            try
            {
                var logger = _serviceProvider.GetService<ILogger<DailySummaryHostedService>>();
                logger?.LogWarning(ex, "DailySummaryHostedService skipped run due to exception (continuing to host).");
            }
            catch { /* ignore logging failures */ }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }
}
