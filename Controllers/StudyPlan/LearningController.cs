using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using Sciencetopia.Services;
using System.Security.Claims;

[ApiController]
[Route("api/StudyPlan/LearningLessons")]
public class StudyPlanController : ControllerBase
{
    private readonly LearningService _learningService;

    public StudyPlanController(LearningService learningService)
    {
        _learningService = learningService;
    }

    [HttpPost("CreateOrUpdateFinishedLearning")]
    public async Task<IActionResult> CreateOrUpdateFinishedLearning([FromBody] LessonResourceDTO lessonResource)
    {
        // Retrieve the current user id (you need to implement this logic)
        string userId = GetCurrentUserId();

        if (lessonResource.Name != null && lessonResource.ResourceLink != null) // Add null check for resourceName
        {
            // Forward the user id, lesson name, and resource link to the ManagePlanService
            await _learningService.CreateFinishedLearningRelationship(lessonResource.Name, lessonResource.ResourceLink, userId);
        }

        return Ok();
    }

    [HttpGet("GetFinishedLearning")]
    public async Task<IActionResult> GetFinishedLearning()
    {
        // Retrieve the current user id (you need to implement this logic)
        string userId = GetCurrentUserId();

        // Forward the user id to the ManagePlanService
        var finishedLearning = await _learningService.GetFinishedLearning(userId);

        return Ok(finishedLearning);
    }

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