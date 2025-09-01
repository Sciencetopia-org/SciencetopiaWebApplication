using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Models;
using Sciencetopia.Data;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.IdentityModel.Tokens.Jwt;

namespace Sciencetopia.Controllers.Admin;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _configuration;

    public AuthController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IConfiguration configuration)
    {
        _context = context;
        _userManager = userManager;
        _signInManager = signInManager;
        _configuration = configuration;
    }

    private async Task<string> GenerateJwtTokenAsync(ApplicationUser user)
    {
        var userRoles = await _userManager.GetRolesAsync(user);

        var authClaims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("AvatarUrl", user.AvatarUrl ?? "")
        };

        foreach (var userRole in userRoles)
        {
            authClaims.Add(new Claim(ClaimTypes.Role, userRole));
        }

        var jwtKey = _configuration["Jwt:Key"];
        if (string.IsNullOrEmpty(jwtKey))
        {
            throw new ArgumentNullException("Jwt:Key", "JWT key is not configured.");
        }
        var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            expires: DateTime.Now.AddMinutes(Convert.ToDouble(_configuration["Jwt:DurationInMinutes"])),
            claims: authClaims,
            signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string GenerateRefreshToken()
    {
        var randomNumber = new byte[32];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }
    }

    // POST: api/Account/AdminLogin
    [HttpPost("AdminLogin")]
    public async Task<IActionResult> AdminLogin(LoginDTO model)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (model.UserName == null)
        {
            return BadRequest("User name is required.");
        }

        var user = await _userManager.FindByNameAsync(model.UserName);
        if (user == null)
        {
            return BadRequest("Invalid login attempt.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        if (!roles.Contains("administrator"))
        {
            return Unauthorized("Only administrators are allowed to log in.");
        }

        var result = await _signInManager.PasswordSignInAsync(model.UserName, model.Password!, model.RememberMe, lockoutOnFailure: false);

        if (result.Succeeded)
        {
            // Generate tokens
            var accessToken = await GenerateJwtTokenAsync(user);
            var refreshToken = GenerateRefreshToken();

            // Calculate expiration time
            var expires = DateTime.Now.AddMinutes(Convert.ToDouble(_configuration["Jwt:DurationInMinutes"]));

            // Return the admin result format
            return Ok(new
            {
                success = true,
                data = new
                {
                    username = user.UserName,
                    roles = roles,  // User roles (administrator, etc.)
                    accessToken = accessToken,  // Generated JWT access token
                    refreshToken = refreshToken,  // Generated refresh token
                    expires = expires  // Token expiration time
                }
            });
        }
        else
        {
            return BadRequest("Invalid login attempt.");
        }
    }
}
