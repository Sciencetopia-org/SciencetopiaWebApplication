using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using Sciencetopia.Models;

namespace Sciencetopia.Controllers.Admin;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "administrator")]
public class AdminBoardController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly StudyGroupService? _studyGroupService;
    private readonly KnowledgeGraphService? _knowledgeGraphService;
    private readonly UserActivityService _userActivityService; // Assuming you have a service to track user activity
    private readonly DailySummaryService _dailySummaryService; // Assuming you have a service to generate daily summaries
    public AdminBoardController(UserManager<ApplicationUser> userManager, StudyGroupService? studyGroupService, KnowledgeGraphService? knowledgeGraphService, UserActivityService userActivityService, DailySummaryService dailySummaryService)
    {
        _userManager = userManager;
        _studyGroupService = studyGroupService;
        _knowledgeGraphService = knowledgeGraphService;
        _userActivityService = userActivityService;
        _dailySummaryService = dailySummaryService;
    }

    // // Retrieve Total Number of Users
    // [HttpGet("GetNumberOfUsers")]
    // public async Task<ActionResult<int>> GetNumberOfUsers()
    // {
    //     var users = await _userManager.GetUsersInRoleAsync("user");
    //     return Ok(users.Count);
    // }

    // // Retrieve Total Number of Study Groups
    // [HttpGet("GetNumberOfStudyGroups")]
    // public async Task<ActionResult<int>> GetNumberOfStudyGroups()
    // {
    //     var groups = await _studyGroupService.GetAllStudyGroups();
    //     return Ok(groups.Count);
    // }

    // // Retrieve Total Number of Knowledge Nodes
    // [HttpGet("GetNumberOfKnowledgeNodes")]
    // public async Task<ActionResult<int>> GetNumberOfKnowledgeNodes()
    // {
    //     var nodes = await _knowledgeGraphService.FetchKnowledgeGraphData();
    //     return Ok(nodes.Count);
    // }

    // // Retrieve Total Visits (总访问量)
    // [HttpGet("GetTotalVisits")]
    // public async Task<ActionResult<int>> GetTotalVisits()
    // {
    //     var totalVisits = await _userActivityService.GetTotalVisits();
    //     return Ok(totalVisits);
    // }

    [HttpGet("GetWeeklySummaryData")]
    public async Task<IActionResult> GetWeeklySummaryData()
    {
        var summary = await _dailySummaryService.GetWeeklySummary();
        return Ok(summary);
    }
}
