using Microsoft.AspNetCore.Mvc;
using Sciencetopia.DTOs;
using Sciencetopia.Services.StudyGroupDiscovery;
using System.Security.Claims;

namespace Sciencetopia.Controllers.StudyGroups;

[ApiController]
[Route("api/StudyGroup")]
public class StudyGroupDiscoveryController : ControllerBase
{
    private readonly IStudyGroupDiscoveryService _discovery;

    public StudyGroupDiscoveryController(IStudyGroupDiscoveryService discovery)
    {
        _discovery = discovery;
    }

    [HttpGet("Recommendations")]
    public async Task<IActionResult> GetRecommendations(
        [FromQuery] StudyGroupRecommendationQuery query,
        CancellationToken ct)
    {
        var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await _discovery.GetRecommendationsAsync(query, userId, ct);
        return Ok(result);
    }

    [HttpGet("SearchSuggestions")]
    public async Task<IActionResult> GetSearchSuggestions(
        [FromQuery] string q,
        [FromQuery] int limit = 8,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(new StudyGroupSearchSuggestionsResponse(Array.Empty<StudyGroupSearchSuggestionDto>()));
        }

        var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await _discovery.GetSearchSuggestionsAsync(q, limit, userId, ct);
        return Ok(result);
    }

    [HttpGet("{groupId:guid}/Related")]
    public async Task<IActionResult> GetRelated(
        Guid groupId,
        [FromQuery] int limit = 6,
        [FromQuery] bool excludeJoined = true,
        CancellationToken ct = default)
    {
        var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await _discovery.GetRelatedAsync(groupId, limit, excludeJoined, userId, ct);
        return Ok(result);
    }

    [HttpPost("RecommendationFeedback")]
    public async Task<IActionResult> RecommendationFeedback(
        [FromBody] StudyGroupRecommendationFeedbackRequest request,
        CancellationToken ct)
    {
        var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        await _discovery.RecordFeedbackAsync(request, userId, ct);
        return Ok(new { success = true });
    }
}
