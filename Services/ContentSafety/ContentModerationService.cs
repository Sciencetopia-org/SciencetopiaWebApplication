using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Sciencetopia.Services.ContentSafety;

public sealed class ContentModerationOptions
{
    public bool Enabled { get; set; } = true;
    public bool AiEnabled { get; set; } = true;
    public bool BlockOnAiUnavailable { get; set; } = true;
    public int MaxTextChars { get; set; } = 20000;
    public string[] SensitiveWords { get; set; } = Array.Empty<string>();
}

public sealed record ContentModerationResult(
    bool Allowed,
    string Reason,
    IReadOnlyList<string> Matches,
    IReadOnlyList<string> BlockedCategories)
{
    public static ContentModerationResult Approved { get; } =
        new(true, "approved", Array.Empty<string>(), Array.Empty<string>());
}

public interface IContentModerationService
{
    Task<ContentModerationResult> ReviewTextAsync(
        IEnumerable<string?> textParts,
        CancellationToken cancellationToken = default);
}

public sealed class ContentModerationService : IContentModerationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IOptions<ContentModerationOptions> _options;
    private readonly ILogger<ContentModerationService> _logger;

    public ContentModerationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IOptions<ContentModerationOptions> options,
        ILogger<ContentModerationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _options = options;
        _logger = logger;
    }

    public async Task<ContentModerationResult> ReviewTextAsync(
        IEnumerable<string?> textParts,
        CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        if (!options.Enabled)
        {
            return ContentModerationResult.Approved;
        }

        var text = BuildReviewText(textParts, options.MaxTextChars);
        if (string.IsNullOrWhiteSpace(text))
        {
            return ContentModerationResult.Approved;
        }

        var keywordMatches = FindSensitiveWords(text, options.SensitiveWords);
        if (keywordMatches.Count > 0)
        {
            return new ContentModerationResult(false, "sensitive_word", keywordMatches, Array.Empty<string>());
        }

        if (!options.AiEnabled)
        {
            return ContentModerationResult.Approved;
        }

        var baseUrl = (_configuration["PythonService:BaseUrl"] ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return options.BlockOnAiUnavailable
                ? new ContentModerationResult(false, "ai_moderation_unavailable", Array.Empty<string>(), Array.Empty<string>())
                : ContentModerationResult.Approved;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient("PythonService");
            client.BaseAddress ??= new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

            var serviceKey = _configuration["PythonService:ServiceKey"];
            if (!string.IsNullOrWhiteSpace(serviceKey))
            {
                client.DefaultRequestHeaders.Remove("X-Service-Key");
                client.DefaultRequestHeaders.Add("X-Service-Key", serviceKey);
            }

            using var response = await client.PostAsJsonAsync(
                "api/moderation/text",
                new { text },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI moderation request failed with status {StatusCode}.", response.StatusCode);
                return options.BlockOnAiUnavailable
                    ? new ContentModerationResult(false, "ai_moderation_unavailable", Array.Empty<string>(), Array.Empty<string>())
                    : ContentModerationResult.Approved;
            }

            var moderation = await response.Content.ReadFromJsonAsync<PythonModerationResponse>(cancellationToken: cancellationToken);
            if (moderation?.Allowed == false || moderation?.Flagged == true)
            {
                return new ContentModerationResult(
                    false,
                    "ai_moderation_rejected",
                    Array.Empty<string>(),
                    moderation.BlockedCategories ?? Array.Empty<string>());
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _logger.LogWarning(ex, "AI moderation request could not be completed.");
            return options.BlockOnAiUnavailable
                ? new ContentModerationResult(false, "ai_moderation_unavailable", Array.Empty<string>(), Array.Empty<string>())
                : ContentModerationResult.Approved;
        }

        return ContentModerationResult.Approved;
    }

    private static string BuildReviewText(IEnumerable<string?> parts, int maxChars)
    {
        var text = string.Join(
            "\n",
            parts
                .Select(part => (part ?? string.Empty).Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        if (maxChars > 0 && text.Length > maxChars)
        {
            return text[..maxChars];
        }

        return text;
    }

    private static List<string> FindSensitiveWords(string text, IEnumerable<string>? sensitiveWords)
    {
        var matches = new List<string>();
        if (sensitiveWords == null)
        {
            return matches;
        }

        foreach (var rawWord in sensitiveWords)
        {
            var word = (rawWord ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }

            if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(word);
            }
        }

        return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private sealed class PythonModerationResponse
    {
        [JsonPropertyName("allowed")]
        public bool Allowed { get; set; } = true;

        [JsonPropertyName("flagged")]
        public bool Flagged { get; set; }

        [JsonPropertyName("blocked_categories")]
        public string[]? BlockedCategories { get; set; }
    }
}
