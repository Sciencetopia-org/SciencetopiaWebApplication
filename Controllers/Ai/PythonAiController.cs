using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Sciencetopia.Controllers.Ai;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class AiController : ControllerBase
{
    private const int MaxStudyPlanNameLength = 500;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public AiController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public sealed record StudyPlanRequest(string? Name);

    [HttpPost("StudyPlan")]
    [EnableRateLimiting("AiOperations")]
    public async Task<IActionResult> GenerateStudyPlan([FromBody] StudyPlanRequest request, CancellationToken ct)
    {
        var name = request?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { error = "Name is required." });
        }

        if (name.Length > MaxStudyPlanNameLength)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = $"Name exceeds {MaxStudyPlanNameLength} characters." });
        }

        var serviceBaseUrl = _configuration["PythonService:BaseUrl"];
        var serviceKey = _configuration["PythonService:ServiceKey"]
            ?? Environment.GetEnvironmentVariable("SCIENCTOPIA_PY_SERVICE_KEY");
        if (string.IsNullOrWhiteSpace(serviceBaseUrl) || string.IsNullOrWhiteSpace(serviceKey) || serviceKey.Length < 32)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "AI service is not configured." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        using var outbound = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(serviceBaseUrl.TrimEnd('/') + "/"), "api/studyplan"))
        {
            Content = new StringContent(JsonSerializer.Serialize(new { Name = name }), Encoding.UTF8, "application/json")
        };
        outbound.Headers.Add("X-Sciencetopia-Service-Key", serviceKey);
        outbound.Headers.Add("X-Sciencetopia-User-Id", userId);
        outbound.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var client = _httpClientFactory.CreateClient("PythonService");
        using var response = await client.SendAsync(outbound, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            return StatusCode((int)response.StatusCode, new { error = "AI service request failed." });
        }

        return Content(body, "application/json", Encoding.UTF8);
    }
}
