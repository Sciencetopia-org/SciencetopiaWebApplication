using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Models;

namespace Sciencetopia.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/tools")] 
    [AllowAnonymous] // guarded by shared secret below; remove this controller after use
    public class AdminToolsController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IConfiguration _configuration;

        public AdminToolsController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, IConfiguration configuration)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _configuration = configuration;
        }

        public record ResetAdminRequest(string email, string newPassword, bool createIfMissing = true);

        [HttpPost("reset")] 
        public async Task<IActionResult> Reset([FromBody] ResetAdminRequest req)
        {
            var providedSecret = Request.Headers["X-Admin-Reset-Secret"].FirstOrDefault()
                                   ?? Request.Query["secret"].FirstOrDefault();
            // Accept from ENV or configuration (appsettings/UserSecrets): ADMIN_RESET_SECRET or Admin:ResetSecret
            var expectedSecret = Environment.GetEnvironmentVariable("ADMIN_RESET_SECRET")
                                   ?? _configuration["ADMIN_RESET_SECRET"]
                                   ?? _configuration["Admin:ResetSecret"];
            if (string.IsNullOrWhiteSpace(expectedSecret) || providedSecret != expectedSecret)
            {
                return Unauthorized("Invalid or missing reset secret.");
            }

            if (string.IsNullOrWhiteSpace(req.email) || string.IsNullOrWhiteSpace(req.newPassword))
            {
                return BadRequest("email and newPassword are required");
            }

            // Ensure admin role exists
            if (!await _roleManager.RoleExistsAsync("administrator"))
            {
                var roleResult = await _roleManager.CreateAsync(new IdentityRole("administrator"));
                if (!roleResult.Succeeded)
                    return StatusCode(500, string.Join("; ", roleResult.Errors.Select(e => e.Description)));
            }

            var user = await _userManager.FindByEmailAsync(req.email);
            if (user == null)
            {
                if (!req.createIfMissing)
                    return NotFound("User not found and createIfMissing=false");

                user = new ApplicationUser { UserName = req.email, Email = req.email, EmailConfirmed = true };
                var create = await _userManager.CreateAsync(user, req.newPassword);
                if (!create.Succeeded)
                    return StatusCode(500, string.Join("; ", create.Errors.Select(e => e.Description)));
            }
            else
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var reset = await _userManager.ResetPasswordAsync(user, token, req.newPassword);
                if (!reset.Succeeded)
                    return StatusCode(500, string.Join("; ", reset.Errors.Select(e => e.Description)));
            }

            if (!await _userManager.IsInRoleAsync(user, "administrator"))
            {
                var addRole = await _userManager.AddToRoleAsync(user, "administrator");
                if (!addRole.Succeeded)
                    return StatusCode(500, string.Join("; ", addRole.Errors.Select(e => e.Description)));
            }

            return Ok(new { message = "Admin reset complete", email = req.email });
        }
    }
}
