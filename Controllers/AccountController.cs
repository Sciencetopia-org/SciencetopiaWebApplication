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
                return Ok();
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

            var user = await ValidateUserAsync(model.UserName, model.Password);
            if (user != null)
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

                    await tx.RunAsync("CREATE (u:User {Id: $Id, UserName: $UserName, Email: $Email, Password: $Password})",
                        new { Id = user.Id, UserName = user.UserName, Email = user.Email, Password = user.Password });

                    return true;
                });

                return result;
            }
        }

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
        public async Task<ApplicationUser> ValidateUserAsync(string userName, string password)
        {
            if (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(password))
            {
                return null; // 或抛出适当的异常
            }

            using (var session = _driver.AsyncSession())
            {
                var user = await session.ReadTransactionAsync(async tx =>
                {
                    var result = await tx.RunAsync("MATCH (u:User {UserName: $UserName}) RETURN u",
                        new { UserName = userName });

                    return await result.PeekAsync();
                });

                if (user != null)
                {
                    string salt = user["Salt"].As<string>() ?? string.Empty;
                    string hashedPassword = HashPassword(password, salt);

                    string userId = user["Id"].As<string>() ?? string.Empty;
                    string userPassword = user["Password"].As<string>() ?? string.Empty;
                    string userEmail = user["Email"].As<string>() ?? string.Empty;

                    if (hashedPassword == userPassword)
                    {
                        return new ApplicationUser
                        {
                            Id = userId,
                            UserName = userName,
                            Email = userEmail,
                            Password = userPassword,
                            Salt = salt
                        };
                    }
                }

                return null;
            }
        }

    }
}