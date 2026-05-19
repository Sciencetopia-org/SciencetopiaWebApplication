namespace Sciencetopia.Services.StudyGroupDiscovery.Vectors;

public sealed record VectorDocument(
    string Collection,
    string Id,
    IReadOnlyList<float> Vector,
    IReadOnlyDictionary<string, string>? Payload = null);

public sealed record VectorSearchResult(
    string Id,
    double Score,
    IReadOnlyDictionary<string, string>? Payload = null);

public interface IEmbeddingService
{
    string Provider { get; }
    string Model { get; }
    Task<IReadOnlyList<float>> EmbedAsync(string input, CancellationToken ct = default);
}

public interface IVectorSearchService
{
    Task UpsertAsync(VectorDocument document, CancellationToken ct = default);
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string collection,
        IReadOnlyList<float> queryVector,
        int topK,
        IReadOnlyDictionary<string, string>? filter = null,
        CancellationToken ct = default);
}

public sealed class NoopEmbeddingService : IEmbeddingService
{
    public string Provider => "noop";
    public string Model => "none";

    public Task<IReadOnlyList<float>> EmbedAsync(string input, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<float>>(Array.Empty<float>());
}

public sealed class NoopVectorSearchService : IVectorSearchService
{
    public Task UpsertAsync(VectorDocument document, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string collection,
        IReadOnlyList<float> queryVector,
        int topK,
        IReadOnlyDictionary<string, string>? filter = null,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<VectorSearchResult>>(Array.Empty<VectorSearchResult>());
}
