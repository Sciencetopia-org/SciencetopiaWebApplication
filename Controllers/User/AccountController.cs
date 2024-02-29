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

namespace Sciencetopia.Controllers
{
    [Route("api/users/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailSender _emailSender;
        private readonly ISmsSender _smsSender;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly IDriver _driver;

        private readonly UserService _userService;

        public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IEmailSender emailSender, ISmsSender smsSender, BlobServiceClient blobServiceClient, IDriver driver, UserService userService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
            _smsSender = smsSender;
            _blobServiceClient = blobServiceClient;
            _driver = driver;
            _userService = userService;
        }

        // POST: api/Account/Register
        [HttpPost("Register")]
        public async Task<IActionResult> Register(RegisterDTO model)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var user = new ApplicationUser { UserName = model.UserName, Email = model.Email };
            var result = await _userManager.CreateAsync(user, model.Password ?? throw new ArgumentNullException(nameof(model.Password)));

            if (result.Succeeded)
            {
                // // Email verification
                // if (model.VerifyByEmail)
                // {
                //     var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                //     var callbackUrl = Url.Action("ConfirmEmail", "Account", new { userId = user.Id, token = token }, protocol: Request.Scheme);
                //     // Send email with this callback URL
                //     await _emailSender.SendEmailAsync(user.Email, "Confirm your email",
                //         $"Please confirm your account by <a href='{callbackUrl}'>clicking here</a>.");
                // }
                // // Phone number verification
                // else
                // {
                //     var token = await _userManager.GenerateChangePhoneNumberTokenAsync(user, user.PhoneNumber);
                //     // Send SMS with the token
                //     await _smsSender.SendSmsAsync(user.PhoneNumber, $"Your verification code is: {token}");
                // }

                // Add user node to Neo4j
                await AddUserNodeToNeo4j(user);

                // SignIn the user
                await _signInManager.SignInAsync(user, isPersistent: false);
                return Ok(new { success = true });
            }

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
                return BadRequest("User ID and token are required.");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (result.Succeeded)
            {
                return Ok("Email confirmed successfully.");
            }
            else
            {
                return BadRequest("Error confirming email.");
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
            if (user == null) return NotFound("User not found.");

            if (model.NewEmail == null)
            {
                return BadRequest("New email is required.");
            }

            var token = await _userManager.GenerateChangeEmailTokenAsync(user, model.NewEmail);
            var callbackUrl = Url.Action("ConfirmEmailChange", "UserInformation",
                new { userId = user.Id, email = model.NewEmail, token = token }, protocol: HttpContext.Request.Scheme);

            // Send email with this callback URL
            await _emailSender.SendEmailAsync(model.NewEmail, "Confirm your new email",
                $"Please confirm your new email by <a href='{callbackUrl}'>clicking here</a>.");

            return Ok("Confirmation link to change email sent to new email address.");
        }

        [HttpGet("ConfirmEmailChange")]
        public async Task<IActionResult> ConfirmEmailChange(string userId, string email, string token)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound("User not found.");

            var result = await _userManager.ChangeEmailAsync(user, email, token);
            if (!result.Succeeded)
            {
                return BadRequest("Error changing email.");
            }

            return Ok("Email changed successfully.");
        }

        [HttpPost("ChangePhoneNumber")]
        public async Task<IActionResult> ChangePhoneNumber(ChangePhoneNumberDTO model)
        {
            if (model.NewPhoneNumber == null)
            {
                return BadRequest("New phone number is required.");
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            var token = await _userManager.GenerateChangePhoneNumberTokenAsync(user, model.NewPhoneNumber);
            await _smsSender.SendSmsAsync(model.NewPhoneNumber, $"Your verification code is: {token}");

            return Ok("Verification code sent.");
        }

        [HttpPost("VerifyNewPhoneNumber")]
        public async Task<IActionResult> VerifyNewPhoneNumber(VerifyPhoneNumberDTO model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            if (model.PhoneNumber == null)
            {
                return BadRequest("Phone number is required.");
            }

            if (model.Token == null)
            {
                return BadRequest("Token is required.");
            }

            var result = await _userManager.ChangePhoneNumberAsync(user, model.PhoneNumber, model.Token);
            if (result.Succeeded)
            {
                return Ok("Phone number changed successfully.");
            }
            else
            {
                return BadRequest("Failed to change phone number.");
            }
        }

        [HttpPost("ChangePassword")]
        public async Task<IActionResult> ChangePassword(ChangePasswordDTO model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            if (model.CurrentPassword == null)
            {
                return BadRequest("Current password is required.");
            }

            if (model.NewPassword == null)
            {
                return BadRequest("New password is required.");
            }

            var result = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
            if (result.Succeeded)
            {
                return Ok("Password changed successfully.");
            }
            else
            {
                return BadRequest("Failed to change password.");
            }
        }

        [HttpPost("ForgotUsername")]
        public async Task<IActionResult> ForgotUsername(ForgotUsernameDTO model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email ?? string.Empty) ??
                       (!string.IsNullOrEmpty(model.PhoneNumber) ? await _userManager.FindByPhoneNumberAsync(model.PhoneNumber) : null);

            if (user != null)
            {
                if (user.Email != null)
                {
                    // Send the username via email or SMS
                    await _emailSender.SendEmailAsync(user.Email, "Your Username", $"Your username is: {user.UserName}");
                    // If using SMS, implement similar logic
                }
                else
                {
                    return BadRequest("User email is null.");
                }
            }
            else
            {
                return NotFound("User not found.");
            }

            return Ok("Username sent successfully.");
        }

        [HttpPost("ForgotPassword")]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordDTO model)
        {
            var user = await _userManager.FindByEmailAsync(model.Email ?? string.Empty) ??
                       (model.PhoneNumber != null ? await _userManager.FindByPhoneNumberAsync(model.PhoneNumber) : null);

            if (user != null)
            {
                if (user.Email != null)
                {
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user);

                    // Send password reset email or SMS
                    var callbackUrl = Url.Action("ResetPassword", "Account",
                        new { userId = user.Id, token = token }, protocol: Request.Scheme);
                    await _emailSender.SendEmailAsync(user.Email, "Reset Password",
                        $"Please reset your password by clicking here: <a href='{callbackUrl}'>link</a>");
                    // If using SMS, send token or URL
                }
                else
                {
                    return BadRequest("User email is null.");
                }
            }
            else
            {
                return NotFound("User not found.");
            }

            return Ok("Password reset instructions sent successfully.");
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

        private async Task<bool> AddUserNodeToNeo4j(ApplicationUser user)
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

                    await tx.RunAsync("CREATE (u:User {id: $Id})",
                        new { Id = user.Id });

                    return true;
                });

                return result;
            }
        }
    }
}