using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using Sciencetopia.Models;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "administrator")]
public class UserRoleController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UserRoleController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    [HttpGet("GetAdministrators")]
    public async Task<IActionResult> GetAllAdministrators()
    {
        var administrators = await _userManager.GetUsersInRoleAsync("administrator");
        return Ok(administrators);
    }
}