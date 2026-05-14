using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Sciencetopia.Services;
using Sciencetopia.Services.Progress;
using System.Security.Claims;
using Sciencetopia.DTOs;

namespace Sciencetopia.Controllers.StudyPlan;

[ApiController]
[Route("api/StudyPlan/LearningLessons")]
public class LearningController : ControllerBase
{
    private readonly LearningService _learningService;
    private readonly IResourceProgressService _progress;

    public LearningController(LearningService learningService, IResourceProgressService progress)
    {
        _learningService = learningService;
        _progress = progress;
    }

    [HttpPost("ToggleFinishedLearning")]
    public async Task<IActionResult> ToggleFinishedLearning([FromBody] ToggleFinishedLearningRequest request)
    {
        string userId = GetCurrentUserId();

        var link = request?.Link ?? request?.ResourceLink;
        if (!string.IsNullOrWhiteSpace(link))
        {
            await _progress.ToggleByLinkAsync(userId, link!, request?.Source, request?.Device);
        }

        return Ok();
    }

    private string GetCurrentUserId()
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return currentUserId ?? string.Empty;
    }
}
