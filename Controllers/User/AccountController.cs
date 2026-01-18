using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Models;
using Sciencetopia.Services;
using System.Threading.Tasks;
using Neo4j.Driver;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Extensions;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Newtonsoft.Json;
using Sciencetopia.Data;

namespace Sciencetopia.Controllers.Users
{
    [Route("api/users/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailSender _emailSender;
        private readonly ISmsSender _smsSender;
        private readonly string _weChatAppId;
        private readonly string _weChatAppSecret;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly IDriver _driver;
        private readonly ApplicationDbContext _dbContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly UserService _userService;
        private readonly EmailTemplateService _emailTemplateService;
        private readonly IWebHostEnvironment _env;

        public AccountController(IConfiguration configuration, IHttpClientFactory httpClientFactory, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IEmailSender emailSender, ISmsSender smsSender, BlobServiceClient blobServiceClient, IDriver driver, ApplicationDbContext dbContext, UserService userService, EmailTemplateService emailTemplateService, IWebHostEnvironment env)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _smsSender = smsSender;
            _blobServiceClient = blobServiceClient;
            _driver = driver;
            _dbContext = dbContext;
            _userService = userService;

            _httpClientFactory = httpClientFactory;
            _emailTemplateService = emailTemplateService;

            // 从配置文件中加载微信 AppId 和 AppSecret
            _weChatAppId = configuration["WeChat:AppId"];
            _weChatAppSecret = configuration["WeChat:AppSecret"];
            _env = env;
        }

        [HttpPost("Register")]
        public async Task<IActionResult> Register(RegisterDTO model)
        {
            // 验证输入是否有效
            if (!ModelState.IsValid) return BadRequest(ModelState);

            // 检查用户名是否已存在
            var existingUserByUsername = await _userManager.FindByNameAsync(model.UserName);
            if (existingUserByUsername != null)
            {
                return BadRequest("该用户名已被使用，请选择其他用户名。");
            }

            // 检查邮箱是否已存在
            var existingUserByEmail = await _userManager.FindByEmailAsync(model.Email);
            if (existingUserByEmail != null)
            {
                return BadRequest("该邮箱已被使用，请使用其他邮箱。");
            }

            // 创建用户 with RegisteredAt
            var user = new ApplicationUser
            {
                UserName = model.UserName,
                Email = model.Email,
                RegisteredAt = DateTime.UtcNow // Save registration time here
            };

            var result = await _userManager.CreateAsync(user, model.Password ?? throw new ArgumentNullException(nameof(model.Password)));

            if (result.Succeeded)
            {
                // Assign the "user" role to the new user
                await _userManager.AddToRoleAsync(user, "user");

                // 生成邮箱确认令牌
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

                // 构建确认链接
                var callbackUrl = Url.Action(
                    "ConfirmEmail", "Account",
                    new { userId = user.Id, token = token },
                    protocol: HttpContext.Request.Scheme);

                // 加载邮箱确认的HTML模板
                var footerImagePath = Url.Content("~/images/logoBanner.png");
                string emailTemplate = await _emailTemplateService.LoadTemplateAsync("ConfirmEmailRegistration");
                string emailContent = _emailTemplateService.PopulateTemplate(emailTemplate, new Dictionary<string, string>
        {
            { "UserName", user.UserName },
            { "CallbackUrl", callbackUrl },
            { "FooterImageUrl", footerImagePath }
        });

                // 发送确认邮件
                await _emailSender.SendEmailAsync(model.Email, "确认您的邮箱", emailContent);

                // 添加用户节点到 Neo4j
                await AddUserNodeToNeo4jAndCreateDefaultFavorite(user);

                // 登陆用户
                await _signInManager.SignInAsync(user, isPersistent: false);

                return Ok(new { success = true });
            }

            // 如果注册失败，返回错误信息
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return BadRequest(ModelState);
        }

        // POST: api/Account/Login
        [HttpPost("Login")]
        public async Task<IActionResult> Login(LoginDTO model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            if (model.UserName == null)
            {
                return BadRequest("User name is required.");
            }

            var result = await _signInManager.PasswordSignInAsync(model.UserName, model.Password!, model.RememberMe, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                return Ok(new { success = true });
            }
            else
            {
                return BadRequest("Invalid login attempt.");
            }
        }

        // POST: api/Account/Logout
        [HttpPost("Logout")]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return Ok(new { success = true });
        }

        [HttpGet("ConfirmEmail")]
        public async Task<IActionResult> ConfirmEmail(string userId, string token)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
            {
                return BadRequest("用户 ID 和令牌是必填项。");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound("未找到用户。");
            }

            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (result.Succeeded)
            {
                return Ok("邮箱确认成功。");
            }
            else
            {
                return BadRequest("邮箱确认失败。");
            }
        }

        [HttpPost("VerifyPhoneNumber")]
        public async Task<IActionResult> VerifyPhoneNumber(string phoneNumber, string code)
        {
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber);
            if (user != null)
            {
                var result = await _userManager.ChangePhoneNumberAsync(user, phoneNumber, code);
                if (result.Succeeded)
                {
                    return Ok("Phone number verified successfully.");
                }
                else
                {
                    return BadRequest("Error verifying phone number.");
                }
            }
            else
            {
                return NotFound("User not found.");
            }
        }

        [HttpPost("ChangeEmail")]
        public async Task<IActionResult> ChangeEmail(ChangeEmailDTO model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound("未找到用户。");

            if (string.IsNullOrWhiteSpace(model.NewEmail))
            {
                return BadRequest("新邮箱地址是必填项。");
            }

            // Check if the email is already in use
            var existingUser = await _userManager.FindByEmailAsync(model.NewEmail);
            if (existingUser != null && existingUser.Id != user.Id)
            {
                return BadRequest("该邮箱地址已被其他用户使用。");
            }

            var token = await _userManager.GenerateChangeEmailTokenAsync(user, model.NewEmail);
            var callbackUrl = Url.Action(
                action: "ConfirmEmailChange",
                controller: "Account",
                values: new { userId = user.Id, email = model.NewEmail, token = token },
                protocol: Request.Scheme);

            var footerImageUrl = Url.Content("~/images/logoBanner.png"); // Adjust this path

            // Load the email template and replace placeholders
            var emailTemplate = await _emailTemplateService.LoadTemplateAsync("ConfirmEmail");
            var emailContent = _emailTemplateService.PopulateTemplate(emailTemplate, new Dictionary<string, string>
    {
        { "UserName", user.UserName },
        { "CallbackUrl", callbackUrl },
        { "FooterImageUrl", footerImageUrl }
    });

            // Send the email
            await _emailSender.SendEmailAsync(model.NewEmail, "确认您的新邮箱", emailContent);

            return Ok("更改邮箱的确认链接已发送至新邮箱地址。");
        }

        [HttpGet("ConfirmEmailChange")]
        public async Task<IActionResult> ConfirmEmailChange(string userId, string email, string token)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return PhysicalFile(Path.Combine(_env.WebRootPath, "EmailTemplates", "EmailChangeFailed.html"), "text/html");
            }

            var result = await _userManager.ChangeEmailAsync(user, email, token);
            if (!result.Succeeded)
            {
                return PhysicalFile(Path.Combine(_env.WebRootPath, "EmailTemplates", "EmailChangeFailed.html"), "text/html");
            }

            return PhysicalFile(Path.Combine(_env.WebRootPath, "EmailTemplates", "EmailChangeSuccess.html"), "text/html");
        }

        [HttpPost("ChangePhoneNumber")]
        public async Task<IActionResult> ChangePhoneNumber(ChangePhoneNumberDTO model)
        {
            if (string.IsNullOrWhiteSpace(model.NewPhoneNumber))
            {
                return BadRequest(new { message = "新电话号码是必填项。" });
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound(new { message = "未找到用户。" });
            }

            // Check if the new phone number is different from the current one
            if (user.PhoneNumber == model.NewPhoneNumber)
            {
                return BadRequest(new { message = "新电话号码与当前号码相同。" });
            }

            // Generate the token and send it via SMS
            var token = await _userManager.GenerateChangePhoneNumberTokenAsync(user, model.NewPhoneNumber);
            await _smsSender.SendSmsAsync(model.NewPhoneNumber, $"您的验证码是: {token}");

            return Ok(new { message = "验证码已发送到新电话号码。" });
        }

        [HttpPost("VerifyNewPhoneNumber")]
        public async Task<IActionResult> VerifyNewPhoneNumber(VerifyPhoneNumberDTO model)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return BadRequest(new { message = "验证失败。", errors });
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound(new { message = "未找到用户。" });
            }

            if (string.IsNullOrWhiteSpace(model.PhoneNumber))
            {
                return BadRequest(new { message = "电话号码是必填项。" });
            }

            if (string.IsNullOrWhiteSpace(model.Token))
            {
                return BadRequest(new { message = "验证码是必填项。" });
            }

            var result = await _userManager.ChangePhoneNumberAsync(user, model.PhoneNumber, model.Token);
            if (result.Succeeded)
            {
                return Ok(new { message = "电话号码更改成功。" });
            }

            var errorMessages = result.Errors.Select(e => e.Description).ToList();
            return BadRequest(new { message = "电话号码更改失败。", errors = errorMessages });
        }

        [HttpGet("BindWeChat")]
        public async Task<IActionResult> BindWeChat(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return BadRequest("授权码不能为空。");
            }

            // 创建 HttpClient 实例
            var client = _httpClientFactory.CreateClient();

            // Step 1: 使用 code 换取 access_token 和 openid
            var tokenUrl = $"https://api.weixin.qq.com/sns/oauth2/access_token?appid={_weChatAppId}&secret={_weChatAppSecret}&code={code}&grant_type=authorization_code";
            var tokenResponse = await client.GetStringAsync(tokenUrl);

            var tokenData = JsonConvert.DeserializeObject<WeChatTokenResponse>(tokenResponse);
            if (tokenData == null || string.IsNullOrEmpty(tokenData.OpenId))
            {
                return BadRequest("获取微信用户信息失败。");
            }

            // Step 2: 使用 access_token 和 openid 获取用户信息
            var userInfoUrl = $"https://api.weixin.qq.com/sns/userinfo?access_token={tokenData.AccessToken}&openid={tokenData.OpenId}";
            var userInfoResponse = await client.GetStringAsync(userInfoUrl);

            var weChatUser = JsonConvert.DeserializeObject<WeChatUserInfo>(userInfoResponse);
            if (weChatUser == null)
            {
                return BadRequest("获取微信用户信息失败。");
            }

            // Step 3: 将微信用户信息与当前账号绑定
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound("用户不存在。");
            }

            // 假设 ApplicationUser 有 WeChatOpenId 字段
            user.WeChatOpenId = weChatUser.OpenId;
            var result = await _userManager.UpdateAsync(user);

            if (!result.Succeeded)
            {
                return BadRequest("绑定微信账号失败。");
            }

            return Ok("微信账号绑定成功！");
        }

        // [HttpPost("RetrievePassword")]
        // public async Task<IActionResult> RetrievePassword(RetrievePasswordDTO model)
        // {
        //     if (string.IsNullOrWhiteSpace(model.Email))
        //     {
        //         return BadRequest("Email is required.");
        //     }

        //     var user = await _userManager.FindByEmailAsync(model.Email);
        //     if (user == null)
        //     {
        //         // Do not reveal that the user does not exist.
        //         return Ok("If an account with that email exists, a password reset link has been sent.");
        //     }

        //     var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        //     var callbackUrl = Url.Action("ResetPassword", "Account", new { userId = user.Id, token = token }, protocol: HttpContext.Request.Scheme);

        //     await _emailSender.SendEmailAsync(model.Email, "Reset Password",
        //         $"Please reset your password by <a href='{callbackUrl}'>clicking here</a>.");

        //     return Ok("If an account with that email exists, a password reset link has been sent.");
        // }

        [HttpPost("ResetPassword")]
        public async Task<IActionResult> ResetPassword(ResetPasswordDTO model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var user = model.UserId != null ? await _userManager.FindByIdAsync(model.UserId) : null;
            if (user == null)
            {
                return BadRequest("Invalid password reset request.");
            }

            var result = await _userManager.ResetPasswordAsync(user, model.Token, model.NewPassword);
            if (result.Succeeded)
            {
                return Ok("Password has been reset successfully.");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return BadRequest(ModelState);
        }

        [HttpPost("ChangePassword")]
        public async Task<IActionResult> ChangePassword(ChangePasswordDTO model)
        {
            if (!ModelState.IsValid)
            {
                // Return validation errors as a structured response
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return BadRequest(new { message = "Validation failed.", errors });
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            if (string.IsNullOrWhiteSpace(model.CurrentPassword))
            {
                return BadRequest(new { message = "Current password is required." });
            }

            if (string.IsNullOrWhiteSpace(model.NewPassword))
            {
                return BadRequest(new { message = "New password is required." });
            }

            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (result.Succeeded)
            {
                return Ok("Password changed successfully.");
            }

            // Return detailed errors if the password change fails
            var errorMessages = result.Errors.Select(e => e.Description).ToList();
            return BadRequest(new { message = "Failed to change password.", errors = errorMessages });
        }

        // GET: /users/Account/GetPasswordStrength
        [HttpGet("GetPasswordStrength")]
        public async Task<IActionResult> GetPasswordStrength()
        {
            // Assuming you want to check the strength of the current user's password
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            // Fetch the password hash from the user object
            var passwordHash = user.PasswordHash;
            if (string.IsNullOrEmpty(passwordHash))
            {
                return BadRequest("Password not found.");
            }

            // Calculate the password strength
            var strength = CalculatePasswordStrength(passwordHash);

            return Ok(new { strength });
        }

        // A simple method to calculate password strength
        private string CalculatePasswordStrength(string password)
        {
            // Example criteria for password strength
            int score = 0;

            if (password.Length >= 8) score++; // Length criterion
            if (password.Any(char.IsUpper)) score++; // Uppercase letter criterion
            if (password.Any(char.IsLower)) score++; // Lowercase letter criterion
            if (password.Any(char.IsDigit)) score++; // Digit criterion
            if (password.Any(ch => "!@#$%^&*()_-+=<>?".Contains(ch))) score++; // Special character criterion

            return score switch
            {
                1 => "弱",
                2 or 3 => "中",
                >= 4 => "强",
                _ => "未知"
            };
        }

        [HttpPost("ForgotUsername")]
        public async Task<IActionResult> ForgotUsername(ForgotUsernameDTO model)
        {
            // 查找用户（通过电子邮件或电话号码）
            var user = await _userManager.FindByEmailAsync(model.Email ?? string.Empty) ??
                       (!string.IsNullOrEmpty(model.PhoneNumber) ? await _userManager.FindByPhoneNumberAsync(model.PhoneNumber) : null);

            if (user != null)
            {
                if (!string.IsNullOrEmpty(user.Email))
                {
                    // 加载找回用户名的HTML模板
                    string emailTemplate = await _emailTemplateService.LoadTemplateAsync("ForgotUsername");

                    // 填充模板中的变量
                    string emailContent = _emailTemplateService.PopulateTemplate(emailTemplate, new Dictionary<string, string>
            {
                { "UserName", user.UserName },
                { "FooterImageUrl", Url.Content("~/images/logoBanner.png") }
            });

                    // 通过电子邮件发送用户名
                    await _emailSender.SendEmailAsync(user.Email, "找回用户名", emailContent);
                }
                else
                {
                    return BadRequest("用户邮箱为空。");
                }
            }
            else
            {
                return NotFound("未找到用户。");
            }

            return Ok("用户名已发送到您的电子邮箱。");
        }

        [HttpPost("ForgotPassword")]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordDTO model)
        {
            // 查找用户（通过电子邮件或电话号码）
            var user = await _userManager.FindByEmailAsync(model.Email ?? string.Empty) ??
                       (!string.IsNullOrEmpty(model.PhoneNumber) ? await _userManager.FindByPhoneNumberAsync(model.PhoneNumber) : null);

            if (user != null)
            {
                if (!string.IsNullOrEmpty(user.Email))
                {
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user);

                    // 构建密码重置链接
                    var callbackUrl = Url.Action("ResetPassword", "Account",
                        new { userId = user.Id, token = token }, protocol: Request.Scheme);

                    // 加载密码重置的HTML模板
                    string emailTemplate = await _emailTemplateService.LoadTemplateAsync("ForgotPassword");

                    // 填充模板中的变量
                    string emailContent = _emailTemplateService.PopulateTemplate(emailTemplate, new Dictionary<string, string>
            {
                { "UserName", user.UserName },
                { "ResetPasswordUrl", callbackUrl },
                { "FooterImageUrl", Url.Content("~/images/logoBanner.png") }
            });

                    // 发送密码重置邮件
                    await _emailSender.SendEmailAsync(user.Email, "找回密码", emailContent);
                }
                else
                {
                    return BadRequest("用户邮箱为空。");
                }
            }
            else
            {
                return NotFound("未找到用户。");
            }

            return Ok("如果该邮箱存在，密码重置链接已发送。");
        }

        [HttpGet("AuthStatus")]
        public IActionResult GetAuthenticationStatus()
        {
            var isAuthenticated = User?.Identity?.IsAuthenticated ?? false;
            return Ok(new { isAuthenticated, userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) });
        }

        [HttpGet("GetAvatarUrl")]
        public async Task<IActionResult> GetAvatarUrl()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return BadRequest("Invalid user ID.");
            }

            // Use the extracted method to fetch the avatar URL
            var avatarUrl = await _userService.FetchUserAvatarUrlByIdAsync(userId);

            if (string.IsNullOrEmpty(avatarUrl))
            {
                // Handle cases where no avatar is set, if necessary
                return Ok(new { AvatarUrl = string.Empty });
            }

            // Return the avatar URL
            return Ok(new { AvatarUrl = avatarUrl });
        }

        private async Task<bool> AddUserNodeToNeo4jAndCreateDefaultFavorite(ApplicationUser user)
        {
            // 1. 在 SQL 中插入默认收藏夹
            var favorite = new Favorite
            {
                UserId = user.Id,
                Name = "默认收藏夹",
                Type = "favorite",
                CreatedAt = DateTime.UtcNow
            };

            var learnedBox = new Favorite
            {
                UserId = user.Id,
                Name = "已学过的知识点",
                Type = "learned",
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Favorites.AddRange(favorite, learnedBox);
            await _dbContext.SaveChangesAsync();

            // 2. 在 Neo4j 中添加 User 和 Favorite 节点及其关系
            using var session = _driver.AsyncSession();

            var result = await session.ExecuteWriteAsync(async tx =>
            {
                // 创建 User 节点（如果不存在）
                await tx.RunAsync(@"
            MERGE (u:User {id: $userId})
            ", new { userId = user.Id });

                // 创建 Favorite 节点并建立关系
                await tx.RunAsync(@"
                    CREATE (f:Favorite {id: $favoriteId, type: $type})
                    WITH f
                    MATCH (u:User {id: $userId})
                    CREATE (u)-[:OWNS]->(f)
                    ",
                    new
                    {
                        userId = user.Id,
                        favoriteId = favorite.Id,
                        type = favorite.Type
                    });

                // 已学过夹
                await tx.RunAsync(@"
                    CREATE (f:Favorite {id: $favoriteId, type: $type})
                    WITH f
                    MATCH (u:User {id: $userId})
                    CREATE (u)-[:OWNS]->(f)
                ", new
                {
                    userId = user.Id,
                    favoriteId = learnedBox.Id,
                    type = learnedBox.Type
                });

                return true;
            });

            return result;
        }
    }
}
