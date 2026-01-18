using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Models;

namespace Sciencetopia.Controllers;

public class HomeController : Controller
{
    private readonly UserActivityService _userActivityService;
    private readonly UserManager<ApplicationUser> _userManager;

    public HomeController(UserActivityService userActivityService, UserManager<ApplicationUser> userManager)
    {
        _userActivityService = userActivityService;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        // Get the logged-in user's ID if available
        var user = await _userManager.GetUserAsync(User);

        // Get the visitor's IP address
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        // Log the visit with IP address
        if (ipAddress != null)
        {
            await _userActivityService.LogVisit(user?.Id, ipAddress);
        }

        return View();
    }
}
