using System.Security.Claims;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sciencetopia.Data;
using Sciencetopia.Models;
using Sciencetopia.Services.ContentSafety;
using Sciencetopia.Services.Messaging;

namespace SciencetopiaWebApplication.Tests;

internal sealed class TestScope : IDisposable
{
    private readonly ServiceProvider _provider;

    public TestScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase($"security-tests-{Guid.NewGuid():N}"));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        _provider = services.BuildServiceProvider();
        Db = _provider.GetRequiredService<ApplicationDbContext>();
        UserManager = _provider.GetRequiredService<UserManager<ApplicationUser>>();
        RoleManager = _provider.GetRequiredService<RoleManager<IdentityRole>>();
        UserService = new UserService(UserManager, new BlobServiceClient("UseDevelopmentStorage=true"));
        AttachmentService = new MessageAttachmentService(new BlobServiceClient("UseDevelopmentStorage=true"));
        NotificationService = new NotificationService(Db);
    }

    public ApplicationDbContext Db { get; }
    public UserManager<ApplicationUser> UserManager { get; }
    public RoleManager<IdentityRole> RoleManager { get; }
    public UserService UserService { get; }
    public MessageAttachmentService AttachmentService { get; }
    public NotificationService NotificationService { get; }

    public async Task AddUserAsync(string id, string userName, bool admin = false)
    {
        var user = new ApplicationUser
        {
            Id = id,
            UserName = userName,
            Email = $"{userName}@example.test"
        };
        var result = await UserManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        if (admin)
        {
            await EnsureRoleAsync("administrator");
            await UserManager.AddToRoleAsync(user, "administrator");
        }
    }

    public Task EnsureRoleAsync(string role)
    {
        var roleManager = _provider.GetRequiredService<RoleManager<IdentityRole>>();
        return roleManager.RoleExistsAsync(role).ContinueWith(async existsTask =>
        {
            if (!existsTask.Result)
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }).Unwrap();
    }

    public void Dispose()
    {
        Db.Dispose();
        _provider.Dispose();
    }
}

internal static class SecurityTestHelpers
{
    public static ClaimsPrincipal Principal(string userId, bool admin = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userId)
        };

        if (admin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "administrator"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    public static void SetUser(this ControllerBase controller, string userId, bool admin = false)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = Principal(userId, admin)
            }
        };
    }
}

internal sealed class AllowAllContentModerationService : IContentModerationService
{
    public Task<ContentModerationResult> ReviewTextAsync(
        IEnumerable<string?> textParts,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ContentModerationResult.Approved);
    }
}
