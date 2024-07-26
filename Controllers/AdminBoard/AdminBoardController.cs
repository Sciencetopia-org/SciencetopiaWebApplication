using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using Sciencetopia.Models;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "administrator")]
public class AdminBoardController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly StudyGroupService? _studyGroupService;
    private readonly KnowledgeGraphService? _knowledgeGraphService;

    public AdminBoardController(UserManager<ApplicationUser> userManager, StudyGroupService? studyGroupService, KnowledgeGraphService? knowledgeGraphService)
    {
        _userManager = userManager;
        _studyGroupService = studyGroupService;
        _knowledgeGraphService = knowledgeGraphService;
    }
    
    [HttpGet("GetNumberOfUsers")]
    public async Task<ActionResult<int>> GetNumberOfUsers()
    {
        var users = await _userManager.GetUsersInRoleAsync("user");
        return Ok(users.Count);
    }

    [HttpGet("GetNumberOfStudyGroups")]
    public async Task<ActionResult<int>> GetNumberOfStudyGroups()
    {
        var groups = await _studyGroupService.GetAllStudyGroups();
        return Ok(groups.Count);
    }

    [HttpGet("GetNumberOfKnowledgeNodes")]
    public async Task<ActionResult<int>> GetNumberOfKnowledgeNodes()
    {
        var nodes = await _knowledgeGraphService.FetchKnowledgeGraphData();
        return Ok(nodes.Count);
    }
}