using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neo4j.Driver;
using Sciencetopia.Models;
using System.Security.Claims;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sciencetopia.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly IDriver _driver;

        public AccountController(IDriver driver)
        {
            _driver = driver;
        }

        // POST: api/Account/Register
        [AllowAnonymous]
        [HttpPost("Register")]
        public async Task<IActionResult> Register(RegisterDTO model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            string salt = Guid.NewGuid().ToString();
            string hashedPassword = HashPassword(model.Password, salt);

            var newUser = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = model.UserName,
                Email = model.Email,
                Password = hashedPassword,
                Salt = salt
            };

            if (await CreateUserAsync(newUser))
            {
                return Ok((new { success = true }));
            }
            else
            {
                return BadRequest(new { error = "用户名已存在。" });
            }
        }

        // POST: api/Account/Login
        [AllowAnonymous]
        [HttpPost("Login")]
        public async Task<IActionResult> Login(LoginDTO model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var result = await ValidateUserAsync(model.UserName, model.Password);
            if (result is OkObjectResult okResult && okResult.Value is Dictionary<string, object> response && response["Status"].ToString() == "Success" && response["User"] is ApplicationUser user)
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.UserName),
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(ClaimTypes.Email, user.Email)
                };

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = model.RememberMe ?? false });

                return Ok();
            }
            else
            {
                return Unauthorized(new { error = "无效的登录尝试。" });
            }
        }

        // POST: api/Account/Logout
        [HttpPost("Logout")]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Ok();
        }

        // 注册用户的逻辑
        [HttpPost("CreateUser")]
        public async Task<bool> CreateUserAsync(ApplicationUser user)
        {
            using (var session = _driver.AsyncSession())
            {
                var result = await session.ExecuteWriteAsync(async tx =>
                {
                    var exists = await tx.RunAsync("MATCH (u:User {UserName: $UserName}) RETURN u", new { UserName = user.UserName });
                    if (await exists.PeekAsync() != null)
                    {
                        return false;
                    }

                    await tx.RunAsync("CREATE (u:User {Id: $Id, UserName: $UserName, Email: $Email, Password: $Password, Salt: $Salt})",
                        new { Id = user.Id, UserName = user.UserName, Email = user.Email, Password = user.Password, Salt = user.Salt });

                    return true;
                });

                return result;
            }
        }

        [HttpPost("HashPassword")]
        public string HashPassword(string password, string salt)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(salt))
            {
                throw new ArgumentException("密码和盐不能为null或空字符串。");
            }

            byte[] saltBytes = Encoding.UTF8.GetBytes(salt);
            byte[] hashedBytes = KeyDerivation.Pbkdf2(password, saltBytes, KeyDerivationPrf.HMACSHA1, 10000, 256 / 8);
            return Convert.ToBase64String(hashedBytes);
        }

        // 验证用户的逻辑
        [HttpPost("ValidateUserAsync")]
        public async Task<IActionResult> ValidateUserAsync(string userName, string password)
        {
            // Check if inputs are valid
            if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
            {
                return BadRequest(new { error = "用户名或密码不能为空" });
            }

            IRecord userNode = null;
            // Fetch the user node from the database
            using var session = _driver.AsyncSession();
            await session.ExecuteReadAsync(async tx =>
            {
                var result = await tx.RunAsync("MATCH (u:User {UserName: $UserName}) RETURN u", new { UserName = userName });
                userNode = await result.PeekAsync(); // Take the first record, if available
            });

            // For debugging: Return the content of userNode
            // return Ok(new { DebugUserNode = userNode });

            // If no user node found, return an unauthorized error
            if (userNode == null)
            {
                return Unauthorized(new { error = "无效的登录尝试。" });
            }

            // Extract properties from the node
            var userProperties = userNode["u"].As<INode>().Properties;
            string salt = userProperties["Salt"].As<string>();
            string userId = userProperties["Id"]?.As<string>();
            string userPassword = userProperties["Password"]?.As<string>();
            string userEmail = userProperties["Email"]?.As<string>();

            // Validate the salt and hashed password
            if (string.IsNullOrEmpty(salt) || string.IsNullOrEmpty(userPassword))
            {
                // Log this error internally and return a generic error to the user.
                return Unauthorized(new { error = "无效的登录尝试。" });
            }

            string hashedPassword = HashPassword(password, salt);
            if (hashedPassword != userPassword)
            {
                return Unauthorized(new { error = "无效的登录尝试。" });
            }

            // If everything is valid, create the application user and return success
            var appUser = new ApplicationUser
            {
                Id = userId,
                UserName = userName,
                Email = userEmail,
                Password = userPassword,
                Salt = salt
            };

            var response = new Dictionary<string, object>
    {
        { "Status", "Success" },
        { "User", appUser }
    };

            return Ok(response);
        }
        
        [HttpGet("IsAuthenticated")]
        public IActionResult IsAuthenticated()
        {
            if (User.Identity.IsAuthenticated)
            {
                return Ok(new { isAuthenticated = true });
            }
            return Ok(new { isAuthenticated = false });
        }

    }
}