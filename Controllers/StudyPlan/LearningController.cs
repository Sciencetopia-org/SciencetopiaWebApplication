using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Sciencetopia.Services;
using System.Security.Claims;
using Sciencetopia.DTOs; 

[ApiController]
[Route("api/StudyPlan/LearningLessons")]
public class StudyPlanController : ControllerBase
{
    private readonly LearningService _learningService;

    public StudyPlanController(LearningService learningService)
    {
        _learningService = learningService;
    }

    [HttpPost("ToggleFinishedLearning")]
    public async Task<IActionResult> ToggleFinishedLearning([FromBody] ToggleFinishedLearningRequest request)
    {
        // Retrieve the current user id (you need to implement this logic)
        string userId = GetCurrentUserId();

        if (!string.IsNullOrWhiteSpace(request?.Link))
        {
            // Toggle (u:User)-[:COMPLETED {at, source, device}]->(r:Resource)
            await _learningService.ToggleFinishedLearningRelationship(request.Link!, userId, request.Source, request.Device);
        }

        return Ok();
    }

    // [HttpGet("GetFinishedLearning")]
    // public async Task<IActionResult> GetFinishedLearning()
    // {
    //     // Retrieve the current user id (you need to implement this logic)
    //     string userId = GetCurrentUserId();

    //     // Forward the user id to the ManagePlanService
    //     var finishedLearning = await _learningService.GetFinishedLearning(userId);

    //     return Ok(finishedLearning);
    // }

    private string GetCurrentUserId()
    {
        // Implement the logic to retrieve the current user id
        // This can vary depending on your authentication/authorization setup
        // You can use HttpContext.User.Identity.Name or any other mechanism
        // to get the current user id
        // For simplicity, let's assume it returns a string
        // Fetch the current authenticated user's ID from claims
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return currentUserId ?? string.Empty;
    }
}
