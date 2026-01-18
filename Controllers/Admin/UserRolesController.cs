using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using Sciencetopia.Models;
using Microsoft.AspNetCore.Authorization;

namespace Sciencetopia.Controllers.Admin;

[Authorize(Roles = "administrator")]
[Route("api/[controller]")]
[ApiController]
public class UserRoleController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public UserRoleController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    // Get user roles
    [HttpGet("{userId}/Roles")]
    public async Task<IActionResult> GetUserRoles(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound("User not found");

        var roles = await _userManager.GetRolesAsync(user);
        return Ok(roles);
    }

    // Assign role to user
    [HttpPost("{userId}/AssignRole")]
    public async Task<IActionResult> AssignRoleToUser(string userId, [FromBody] string role)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound("User not found");

        if (!await _roleManager.RoleExistsAsync(role))
        {
            return BadRequest("Role does not exist");
        }

        var result = await _userManager.AddToRoleAsync(user, role);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        return Ok($"Role '{role}' assigned to user '{user.UserName}'");
    }

    // Remove role from user
    [HttpPost("{userId}/RemoveRole")]
    public async Task<IActionResult> RemoveRoleFromUser(string userId, [FromBody] string role)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound("User not found");

        if (!await _userManager.IsInRoleAsync(user, role))
        {
            return BadRequest("User is not in this role");
        }

        var result = await _userManager.RemoveFromRoleAsync(user, role);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        return Ok($"Role '{role}' removed from user '{user.UserName}'");
    }

    // Get all available roles
    [HttpGet("Roles")]
    public IActionResult GetAllRoles()
    {
        var roles = _roleManager.Roles;
        return Ok(roles);
    }

    // Create a new role
    [HttpPost("CreateRole")]
    public async Task<IActionResult> CreateRole([FromBody] string roleName)
    {
        if (await _roleManager.RoleExistsAsync(roleName))
        {
            return BadRequest("Role already exists");
        }

        var result = await _roleManager.CreateAsync(new IdentityRole(roleName));
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        return Ok($"Role '{roleName}' created successfully");
    }

    // Delete a role
    [HttpDelete("DeleteRole")]
    public async Task<IActionResult> DeleteRole([FromBody] string roleName)
    {
        var role = await _roleManager.FindByNameAsync(roleName);
        if (role == null) return NotFound("Role not found");

        var result = await _roleManager.DeleteAsync(role);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        return Ok($"Role '{roleName}' deleted successfully");
    }

    // Create Admin Account and Assign 'administrator' Role
    [HttpPost("CreateAdmin")]
    public async Task<IActionResult> CreateAdminAccount([FromBody] RegisterDTO model)
    {
        // Check if the administrator role exists, create if not
        if (!await _roleManager.RoleExistsAsync("administrator"))
        {
            var roleResult = await _roleManager.CreateAsync(new IdentityRole("administrator"));
            if (!roleResult.Succeeded) return BadRequest("Failed to create 'administrator' role");
        }

        // Check if the user already exists
        if (string.IsNullOrEmpty(model.Email)) return BadRequest("Email is required");
        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user != null) return BadRequest("User already exists");

        // Create the admin user
        var adminUser = new ApplicationUser { UserName = model.UserName, Email = model.Email };
        if (string.IsNullOrEmpty(model.Password)) return BadRequest("Password is required");
        var createUserResult = await _userManager.CreateAsync(adminUser, model.Password);
        if (!createUserResult.Succeeded) return BadRequest(createUserResult.Errors);

        // Assign 'administrator' role to the user
        var assignRoleResult = await _userManager.AddToRoleAsync(adminUser, "administrator");
        if (!assignRoleResult.Succeeded) return BadRequest(assignRoleResult.Errors);

        return Ok("Admin account created successfully");
    }

}
