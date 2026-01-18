using Microsoft.Extensions.Options;

namespace Sciencetopia.Services.KnowledgeGraph;

public interface IDraftFreezeService
{
    bool IsFrozen { get; }
    string? Message { get; }
    void EnsureDraftingEnabled();
}

public sealed class DraftFreezeOptions
{
    public bool Enabled { get; set; }
    public string? Message { get; set; }
}

public sealed class DraftFreezeService : IDraftFreezeService
{
    private readonly IOptionsMonitor<DraftFreezeOptions> _options;

    public DraftFreezeService(IOptionsMonitor<DraftFreezeOptions> options)
    {
        _options = options;
    }

    public bool IsFrozen => _options.CurrentValue.Enabled;

    public string? Message => _options.CurrentValue.Message;

    public void EnsureDraftingEnabled()
    {
        if (!IsFrozen) return;
        var message = string.IsNullOrWhiteSpace(Message)
            ? "Draft creation is temporarily disabled for maintenance."
            : Message!;
        throw new DraftingFrozenException(message);
    }
}

public sealed class DraftingFrozenException : InvalidOperationException
{
    public DraftingFrozenException(string message) : base(message)
    {
    }
}
