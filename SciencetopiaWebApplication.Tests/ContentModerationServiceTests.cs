using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sciencetopia.Services.ContentSafety;

namespace SciencetopiaWebApplication.Tests;

public class ContentModerationServiceTests
{
    [Fact]
    public async Task Rejects_Configured_Sensitive_Word_Before_Ai_Call()
    {
        var service = CreateService(new ContentModerationOptions
        {
            SensitiveWords = new[] { "测试违禁词" },
            AiEnabled = true,
            BlockOnAiUnavailable = true
        });

        var result = await service.ReviewTextAsync(new[] { "这是一段包含测试违禁词的内容" });

        Assert.False(result.Allowed);
        Assert.Equal("sensitive_word", result.Reason);
        Assert.Contains("测试违禁词", result.Matches);
    }

    [Fact]
    public async Task Rejects_When_Ai_Flags_Text()
    {
        var service = CreateService(
            new ContentModerationOptions
            {
                SensitiveWords = Array.Empty<string>(),
                AiEnabled = true,
                BlockOnAiUnavailable = true
            },
            """{"allowed":false,"flagged":true,"blocked_categories":["violence"]}""");

        var result = await service.ReviewTextAsync(new[] { "review me" });

        Assert.False(result.Allowed);
        Assert.Equal("ai_moderation_rejected", result.Reason);
        Assert.Contains("violence", result.BlockedCategories);
    }

    [Fact]
    public async Task Fails_Closed_When_Ai_Is_Required_But_Unconfigured()
    {
        var service = CreateService(
            new ContentModerationOptions
            {
                SensitiveWords = Array.Empty<string>(),
                AiEnabled = true,
                BlockOnAiUnavailable = true
            },
            pythonBaseUrl: "");

        var result = await service.ReviewTextAsync(new[] { "normal text" });

        Assert.False(result.Allowed);
        Assert.Equal("ai_moderation_unavailable", result.Reason);
    }

    private static ContentModerationService CreateService(
        ContentModerationOptions options,
        string moderationResponseJson = """{"allowed":true,"flagged":false,"blocked_categories":[]}""",
        string pythonBaseUrl = "http://python-api/")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PythonService:BaseUrl"] = pythonBaseUrl
            })
            .Build();

        return new ContentModerationService(
            new FakeHttpClientFactory(moderationResponseJson),
            configuration,
            Options.Create(options),
            NullLogger<ContentModerationService>.Instance);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly string _responseJson;

        public FakeHttpClientFactory(string responseJson)
        {
            _responseJson = responseJson;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new StubHandler(_responseJson));
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public StubHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson)
            });
        }
    }
}
