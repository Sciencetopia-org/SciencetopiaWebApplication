using System.Data;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sciencetopia.Data;

namespace Sciencetopia.Services.SearchEngine;

public sealed record SearchVectorHit(
    string EntityType,
    string EntityId,
    double Score);

public sealed record SearchVectorStatus(
    string Provider,
    string Model,
    string? Endpoint,
    int BatchSize,
    bool ApiKeyConfigured,
    bool SemanticSearchEnabled,
    bool VectorIndexReady,
    int IndexedDocuments,
    IReadOnlyDictionary<string, int> IndexedDocumentsByType);

public sealed class SearchVectorService
{
    public const string KnowledgeNodeType = "knowledge_node";
    public const string ResourceType = "resource";
    public const string StudyGroupType = "study_group";

    private const string LocalProvider = "local";
    private const string LocalModel = "hashing-v1-384";
    private const string OpenAiProvider = "openai";
    private const string OpenAiCompatibleProvider = "openai-compatible";
    private const string DashScopeProvider = "dashscope";
    private const string SiliconFlowProvider = "siliconflow";
    private const string DeepSeekProvider = "deepseek";
    private const string MistralProvider = "mistral";
    private const string DefaultOpenAiEmbeddingModel = "text-embedding-3-small";
    private const string DefaultDashScopeEmbeddingModel = "text-embedding-v4";
    private const string DefaultSiliconFlowEmbeddingModel = "BAAI/bge-m3";
    private const string DefaultMistralEmbeddingModel = "mistral-embed";
    private const int LocalDimensions = 384;
    private const string RefreshCacheKey = "search:vectors:last-refresh";
    private const string IndexedRowsCacheKey = "search:vectors:indexed-rows";
    private const string QueryVectorCacheKey = "search:vectors:query";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IndexedRowsCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan QueryVectorCacheTtl = TimeSpan.FromMinutes(30);

    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SearchVectorService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public SearchVectorService(
        ApplicationDbContext db,
        IMemoryCache cache,
        ILogger<SearchVectorService> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<SearchVectorHit>> SearchAsync(
        string query,
        int limit,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<SearchVectorHit>();
        }

        try
        {
            var embeddingProvider = GetEmbeddingProvider();
            var rows = await LoadIndexedRowsAsync(embeddingProvider.Provider, embeddingProvider.Model, ct);
            if (rows.Count == 0)
            {
                return Array.Empty<SearchVectorHit>();
            }

            var queryVector = await EmbedOneAsync(query, embeddingProvider, ct);

            return rows
                .Select(row => new SearchVectorHit(row.EntityType, row.EntityId, Dot(queryVector, row.Vector)))
                .Where(hit => hit.Score > 0)
                .OrderByDescending(hit => hit.Score)
                .Take(Math.Clamp(limit, 1, 100))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector search failed; falling back to keyword search.");
            return Array.Empty<SearchVectorHit>();
        }
    }

    public async Task<int> RefreshIndexAsync(
        string? query = null,
        int? maxDocuments = null,
        CancellationToken ct = default)
    {
        var embeddingProvider = GetEmbeddingProvider();
        return await RefreshIndexAsync(query ?? string.Empty, embeddingProvider, maxDocuments, ct);
    }

    public async Task<SearchVectorStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var embeddingProvider = GetEmbeddingProvider();
        var counts = await LoadIndexCountsAsync(embeddingProvider.Provider, embeddingProvider.Model, ct);
        var total = counts.Values.Sum();

        return new SearchVectorStatus(
            embeddingProvider.Provider,
            embeddingProvider.Model,
            embeddingProvider.Endpoint,
            embeddingProvider.BatchSize,
            embeddingProvider.ApiKeyConfigured,
            embeddingProvider.IsRemote,
            total > 0,
            total,
            counts);
    }

    private async Task<int> RefreshIndexAsync(
        string query,
        EmbeddingProvider embeddingProvider,
        int? maxDocuments,
        CancellationToken ct)
    {
        var cacheKey = $"{RefreshCacheKey}:{embeddingProvider.Provider}:{embeddingProvider.Model}";
        if (!maxDocuments.HasValue
            && _cache.TryGetValue<DateTimeOffset>(cacheKey, out var lastRefresh)
            && DateTimeOffset.UtcNow - lastRefresh < RefreshInterval)
        {
            return 0;
        }

        var documents = await BuildDocumentsAsync(ct);
        var existingHashes = await LoadExistingHashesAsync(embeddingProvider.Provider, embeddingProvider.Model, ct);
        var maxDocumentsPerRefresh = embeddingProvider.IsRemote
            ? maxDocuments ?? _configuration.GetValue<int?>("Search:Embedding:MaxDocumentsPerRefresh") ?? 64
            : int.MaxValue;
        var staleDocuments = documents
            .Where(document =>
            {
                var key = GetDocumentKey(document.EntityType, document.EntityId);
                return !existingHashes.TryGetValue(key, out var hash) || hash != Hash(document.SourceText);
            })
            .OrderByDescending(document => LexicalScore(query, document.SourceText))
            .Take(maxDocumentsPerRefresh)
            .ToList();

        foreach (var batch in staleDocuments.Chunk(embeddingProvider.BatchSize))
        {
            var vectors = await EmbedBatchAsync(batch.Select(x => x.SourceText).ToList(), embeddingProvider, ct);
            for (var i = 0; i < batch.Length; i++)
            {
                await UpsertDocumentAsync(batch[i], vectors[i], embeddingProvider.Provider, embeddingProvider.Model, ct);
            }
        }

        if (staleDocuments.Count < maxDocumentsPerRefresh)
        {
            _cache.Set(cacheKey, DateTimeOffset.UtcNow, RefreshInterval);
        }

        if (staleDocuments.Count > 0)
        {
            _cache.Remove(GetIndexedRowsCacheKey(embeddingProvider.Provider, embeddingProvider.Model));
        }

        return staleDocuments.Count;
    }

    private async Task<List<SearchDocument>> BuildDocumentsAsync(CancellationToken ct)
    {
        var docs = new List<SearchDocument>();

        var knowledgeNodes = await _db.KnowledgeNodes.AsNoTracking()
            .Where(n => n.StableId != Guid.Empty && n.IsCurrent && n.Status == "Current")
            .Select(n => new
            {
                Id = n.StableId,
                Title = n.Name ?? "",
                Content = n.Description ?? ""
            })
            .ToListAsync(ct);

        docs.AddRange(knowledgeNodes
            .Where(x => !string.IsNullOrWhiteSpace(x.Title) || !string.IsNullOrWhiteSpace(x.Content))
            .Select(x => new SearchDocument(
                KnowledgeNodeType,
                x.Id.ToString(),
                x.Title,
                BuildSourceText("Knowledge node", x.Title, x.Content))));

        var resources = await _db.Resources.AsNoTracking()
            .Where(r => r.Id != Guid.Empty && (r.Name != null || r.Link != null))
            .Select(r => new
            {
                r.Id,
                Title = r.Name ?? r.Link ?? "",
                Content = r.Link ?? ""
            })
            .ToListAsync(ct);

        docs.AddRange(resources
            .Where(x => !string.IsNullOrWhiteSpace(x.Title) || !string.IsNullOrWhiteSpace(x.Content))
            .Select(x => new SearchDocument(
                ResourceType,
                x.Id.ToString(),
                x.Title,
                BuildSourceText("Learning resource", x.Title, x.Content))));

        var studyGroups = await _db.StudyGroups.AsNoTracking()
            .Where(g => g.Id != Guid.Empty
                && (g.Status == null || g.Status == "" || g.Status == "approved" || g.Status == "Approved" || g.Status == "active" || g.Status == "Active"))
            .Select(g => new
            {
                g.Id,
                Title = g.Name,
                Content = g.Description ?? ""
            })
            .ToListAsync(ct);

        docs.AddRange(studyGroups
            .Where(x => !string.IsNullOrWhiteSpace(x.Title) || !string.IsNullOrWhiteSpace(x.Content))
            .Select(x => new SearchDocument(
                StudyGroupType,
                x.Id.ToString(),
                x.Title,
                BuildSourceText("Study group", x.Title, x.Content))));

        return docs;
    }

    private async Task<Dictionary<string, string>> LoadExistingHashesAsync(
        string provider,
        string model,
        CancellationToken ct)
    {
        var rows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT EntityType, EntityId, SourceHash
FROM [Search].[VectorDocuments]
WHERE IsDeleted = 0
  AND Provider = @Provider
  AND Model = @Model;";
        AddParameter(command, "@Provider", provider);
        AddParameter(command, "@Model", model);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows[GetDocumentKey(reader.GetString(0), reader.GetString(1))] = reader.GetString(2);
        }

        return rows;
    }

    private async Task UpsertDocumentAsync(
        SearchDocument document,
        IReadOnlyList<float> vector,
        string provider,
        string model,
        CancellationToken ct)
    {
        var sourceHash = Hash(document.SourceText);
        var vectorJson = JsonSerializer.Serialize(vector);

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
MERGE [Search].[VectorDocuments] AS target
USING (SELECT @EntityType AS EntityType, @EntityId AS EntityId, @Provider AS Provider, @Model AS Model) AS source
ON target.EntityType = source.EntityType
   AND target.EntityId = source.EntityId
   AND target.Provider = source.Provider
   AND target.Model = source.Model
WHEN MATCHED THEN
    UPDATE SET
        Title = @Title,
        Content = @Content,
        SourceHash = @SourceHash,
        VectorJson = @VectorJson,
        UpdatedAt = SYSUTCDATETIME(),
        IsDeleted = 0
WHEN NOT MATCHED THEN
    INSERT (Id, EntityType, EntityId, Title, Content, SourceHash, Provider, Model, VectorJson, UpdatedAt, IsDeleted)
    VALUES (NEWID(), @EntityType, @EntityId, @Title, @Content, @SourceHash, @Provider, @Model, @VectorJson, SYSUTCDATETIME(), 0);";
        AddParameter(command, "@EntityType", document.EntityType);
        AddParameter(command, "@EntityId", document.EntityId);
        AddParameter(command, "@Title", Truncate(document.Title, 512));
        AddParameter(command, "@Content", document.SourceText);
        AddParameter(command, "@SourceHash", sourceHash);
        AddParameter(command, "@Provider", provider);
        AddParameter(command, "@Model", model);
        AddParameter(command, "@VectorJson", vectorJson);

        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<List<IndexedVectorRow>> LoadIndexedRowsAsync(
        string provider,
        string model,
        CancellationToken ct)
    {
        var cacheKey = GetIndexedRowsCacheKey(provider, model);
        if (_cache.TryGetValue<List<IndexedVectorRow>>(cacheKey, out var cachedRows))
        {
            return cachedRows;
        }

        var rows = new List<IndexedVectorRow>();
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT EntityType, EntityId, VectorJson
FROM [Search].[VectorDocuments]
WHERE IsDeleted = 0
  AND Provider = @Provider
  AND Model = @Model
  AND EntityType IN ('knowledge_node', 'resource', 'study_group');";
        AddParameter(command, "@Provider", provider);
        AddParameter(command, "@Model", model);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var vectorJson = reader.GetString(2);
            var vector = JsonSerializer.Deserialize<float[]>(vectorJson) ?? Array.Empty<float>();
            if (vector.Length > 0)
            {
                rows.Add(new IndexedVectorRow(reader.GetString(0), reader.GetString(1), vector));
            }
        }

        _cache.Set(cacheKey, rows, IndexedRowsCacheTtl);
        return rows;
    }

    private async Task<Dictionary<string, int>> LoadIndexCountsAsync(
        string provider,
        string model,
        CancellationToken ct)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [KnowledgeNodeType] = 0,
            [ResourceType] = 0,
            [StudyGroupType] = 0
        };
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT EntityType, COUNT(1)
FROM [Search].[VectorDocuments]
WHERE IsDeleted = 0
  AND Provider = @Provider
  AND Model = @Model
  AND EntityType IN ('knowledge_node', 'resource', 'study_group')
GROUP BY EntityType;";
        AddParameter(command, "@Provider", provider);
        AddParameter(command, "@Model", model);

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return counts;
    }

    private async Task<IReadOnlyList<float>> EmbedOneAsync(
        string text,
        EmbeddingProvider embeddingProvider,
        CancellationToken ct)
    {
        var normalizedText = NormalizeQueryText(text);
        var cacheKey = $"{QueryVectorCacheKey}:{embeddingProvider.Provider}:{embeddingProvider.Model}:{Hash(normalizedText)}";
        if (_cache.TryGetValue<IReadOnlyList<float>>(cacheKey, out var cachedVector))
        {
            return cachedVector;
        }

        var vector = (await EmbedBatchAsync(new[] { normalizedText }, embeddingProvider, ct))[0];
        _cache.Set(cacheKey, vector, QueryVectorCacheTtl);
        return vector;
    }

    private async Task<IReadOnlyList<IReadOnlyList<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        EmbeddingProvider embeddingProvider,
        CancellationToken ct)
    {
        if (texts.Count == 0)
        {
            return Array.Empty<IReadOnlyList<float>>();
        }

        if (!embeddingProvider.IsRemote)
        {
            return texts.Select(EmbedLocal).ToList();
        }

        if (string.IsNullOrWhiteSpace(embeddingProvider.ApiKey) || string.IsNullOrWhiteSpace(embeddingProvider.Endpoint))
        {
            throw new InvalidOperationException(
                $"Embedding provider '{embeddingProvider.Provider}' requires Search:Embedding:ApiKey and Search:Embedding:Endpoint.");
        }

        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, embeddingProvider.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", embeddingProvider.ApiKey);
        request.Content = JsonContent.Create(new
        {
            model = embeddingProvider.Model,
            input = texts,
            encoding_format = "float"
        });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_configuration.GetValue<int?>("Search:Embedding:RequestTimeoutSeconds") ?? 25));
        using var response = await client.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(timeout.Token);
            throw new HttpRequestException(
                $"Embedding provider '{embeddingProvider.Provider}' returned {(int)response.StatusCode} ({response.ReasonPhrase}). Response body: {Truncate(responseBody, 2000)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
        var vectors = json.RootElement
            .GetProperty("data")
            .EnumerateArray()
            .OrderBy(item => item.GetProperty("index").GetInt32())
            .Select(item => Normalize(item.GetProperty("embedding").EnumerateArray().Select(x => x.GetSingle()).ToArray()))
            .Cast<IReadOnlyList<float>>()
            .ToList();

        if (vectors.Count != texts.Count)
        {
            throw new InvalidOperationException("Embedding response count does not match request count.");
        }

        return vectors;
    }

    private EmbeddingProvider GetEmbeddingProvider()
    {
        var configuredProvider = _configuration["Search:Embedding:Provider"];
        var provider = NormalizeProvider(configuredProvider);

        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = InferProviderFromConfiguredKeys();
        }

        if (provider == LocalProvider)
        {
            return new EmbeddingProvider(LocalProvider, LocalModel, null, null, int.MaxValue, false, false);
        }

        var endpoint = _configuration["Search:Embedding:Endpoint"]
                       ?? _configuration[$"Search:Embedding:Providers:{provider}:Endpoint"]
                       ?? GetDefaultEndpoint(provider);
        var model = _configuration["Search:Embedding:Model"]
                    ?? _configuration[$"Search:Embedding:Providers:{provider}:Model"]
                    ?? GetDefaultModel(provider);
        var apiKey = GetApiKey(provider);
        var batchSize = GetBatchSize(provider);
        var isRemote = !string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(model);

        return new EmbeddingProvider(
            provider,
            string.IsNullOrWhiteSpace(model) ? LocalModel : model,
            endpoint,
            apiKey,
            batchSize,
            !string.IsNullOrWhiteSpace(apiKey),
            isRemote);
    }

    private string InferProviderFromConfiguredKeys()
    {
        if (!string.IsNullOrWhiteSpace(GetApiKey(OpenAiProvider)))
        {
            return OpenAiProvider;
        }

        if (!string.IsNullOrWhiteSpace(GetApiKey(DashScopeProvider)))
        {
            return DashScopeProvider;
        }

        if (!string.IsNullOrWhiteSpace(GetApiKey(SiliconFlowProvider)))
        {
            return SiliconFlowProvider;
        }

        if (!string.IsNullOrWhiteSpace(GetApiKey(MistralProvider)))
        {
            return MistralProvider;
        }

        return LocalProvider;
    }

    private static string NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return string.Empty;
        }

        return provider.Trim().ToLowerInvariant() switch
        {
            "custom" => OpenAiCompatibleProvider,
            "qwen" => DashScopeProvider,
            "bailian" => DashScopeProvider,
            "aliyun" => DashScopeProvider,
            "aliyun-bailian" => DashScopeProvider,
            "openai_compatible" => OpenAiCompatibleProvider,
            "openai-compatible" => OpenAiCompatibleProvider,
            var value => value
        };
    }

    private static string? GetDefaultEndpoint(string provider)
        => provider switch
        {
            OpenAiProvider => "https://api.openai.com/v1/embeddings",
            DashScopeProvider => "https://dashscope.aliyuncs.com/compatible-mode/v1/embeddings",
            SiliconFlowProvider => "https://api.siliconflow.cn/v1/embeddings",
            MistralProvider => "https://api.mistral.ai/v1/embeddings",
            // DeepSeek is left configurable because its public API is primarily chat-focused.
            DeepSeekProvider => null,
            OpenAiCompatibleProvider => null,
            _ => null
        };

    private static string? GetDefaultModel(string provider)
        => provider switch
        {
            OpenAiProvider => DefaultOpenAiEmbeddingModel,
            DashScopeProvider => DefaultDashScopeEmbeddingModel,
            SiliconFlowProvider => DefaultSiliconFlowEmbeddingModel,
            MistralProvider => DefaultMistralEmbeddingModel,
            DeepSeekProvider => null,
            OpenAiCompatibleProvider => null,
            _ => null
        };

    private string? GetApiKey(string provider)
        => _configuration["Search:Embedding:ApiKey"]
           ?? _configuration[$"Search:Embedding:Providers:{provider}:ApiKey"]
           ?? provider switch
           {
               OpenAiProvider => _configuration["OpenAIServiceOptions:ApiKey"]
                                 ?? _configuration["OpenAI:ApiKey"]
                                 ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
               DashScopeProvider => Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY")
                                    ?? Environment.GetEnvironmentVariable("QWEN_API_KEY")
                                    ?? Environment.GetEnvironmentVariable("ALIYUN_BAILIAN_API_KEY"),
               SiliconFlowProvider => Environment.GetEnvironmentVariable("SILICONFLOW_API_KEY"),
               MistralProvider => Environment.GetEnvironmentVariable("MISTRAL_API_KEY"),
               DeepSeekProvider => Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"),
               _ => Environment.GetEnvironmentVariable("EMBEDDING_API_KEY")
           };

    private int GetBatchSize(string provider)
    {
        var configured = _configuration.GetValue<int?>("Search:Embedding:BatchSize")
                         ?? _configuration.GetValue<int?>($"Search:Embedding:Providers:{provider}:BatchSize");
        var defaultBatchSize = provider == DashScopeProvider ? 10 : 64;
        return Math.Clamp(configured ?? defaultBatchSize, 1, 64);
    }

    private static float[] EmbedLocal(string text)
    {
        var vector = new float[LocalDimensions];
        foreach (var token in Tokenize(text))
        {
            var hash = StableHash(token);
            var index = (int)(hash % LocalDimensions);
            var sign = (hash & 1) == 0 ? 1f : -1f;
            vector[index] += sign;
        }

        return Normalize(vector);
    }

    private static float[] Normalize(float[] vector)
    {
        var norm = Math.Sqrt(vector.Sum(x => x * x));
        if (norm <= 0)
        {
            return vector;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }

        return vector;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var normalized = Regex.Replace(text.ToLowerInvariant(), "<.*?>", " ");
        foreach (Match match in Regex.Matches(normalized, @"[\p{L}\p{N}]{2,}"))
        {
            var token = match.Value;
            yield return token;

            if (HasCjk(token))
            {
                for (var i = 0; i < token.Length - 1; i++)
                {
                    yield return token.Substring(i, 2);
                }
            }
        }
    }

    private static bool HasCjk(string value)
        => value.Any(ch => ch >= '\u4e00' && ch <= '\u9fff');

    private static double LexicalScore(string query, string text)
    {
        var queryTokens = Tokenize(query).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToList();
        if (queryTokens.Count == 0)
        {
            return 0;
        }

        var textTokens = Tokenize(text).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return queryTokens.Count(textTokens.Contains) / (double)queryTokens.Count;
    }

    private static ulong StableHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToUInt64(bytes, 0);
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private static double Dot(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        var sum = 0d;
        for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
        {
            sum += left[i] * right[i];
        }

        return sum;
    }

    private static string BuildSourceText(string kind, string? title, string? content)
        => $"{kind}\nTitle: {title ?? ""}\nContent: {content ?? ""}".Trim();

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static string GetDocumentKey(string entityType, string entityId)
        => $"{entityType}:{entityId}";

    private static string GetIndexedRowsCacheKey(string provider, string model)
        => $"{IndexedRowsCacheKey}:{provider}:{model}";

    private static string NormalizeQueryText(string text)
        => Regex.Replace(text.Trim().ToLowerInvariant(), @"\s+", " ");

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed record SearchDocument(
        string EntityType,
        string EntityId,
        string Title,
        string SourceText);

    private sealed record IndexedVectorRow(
        string EntityType,
        string EntityId,
        IReadOnlyList<float> Vector);

    private sealed record EmbeddingProvider(
        string Provider,
        string Model,
        string? Endpoint,
        string? ApiKey,
        int BatchSize,
        bool ApiKeyConfigured,
        bool IsRemote);
}
