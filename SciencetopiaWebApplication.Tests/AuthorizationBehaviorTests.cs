using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sciencetopia.Controllers.StudyPlan;
using Sciencetopia.Controllers.Users;
using Sciencetopia.DTOs;
using Sciencetopia.Models;
using Sciencetopia.Services;
using Sciencetopia.Services.Plans;
using Sciencetopia.Services.Progress;

namespace SciencetopiaWebApplication.Tests;

public class AuthorizationBehaviorTests
{
    [Fact]
    public async Task Anonymous_Cannot_Read_PrivateStudyPlan()
    {
        using var scope = new TestScope();
        var planId = await AddStudyPlanAsync(scope, Guid.NewGuid().ToString(), "private");
        var controller = CreateStudyPlansController(scope);

        var result = await controller.GetById(planId);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task User_Cannot_Read_AnotherUsersPrivateStudyPlan()
    {
        using var scope = new TestScope();
        var ownerId = Guid.NewGuid().ToString();
        var otherId = Guid.NewGuid().ToString();
        await scope.AddUserAsync(ownerId, "owner");
        await scope.AddUserAsync(otherId, "other");
        var planId = await AddStudyPlanAsync(scope, ownerId, "private");
        var controller = CreateStudyPlansController(scope, otherId);

        var result = await controller.GetById(planId);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task ResourceOwner_Can_Read_PrivateStudyPlan()
    {
        using var scope = new TestScope();
        var ownerId = Guid.NewGuid().ToString();
        await scope.AddUserAsync(ownerId, "owner");
        var planId = await AddStudyPlanAsync(scope, ownerId, "private");
        var controller = CreateStudyPlansController(scope, ownerId);

        var result = await controller.GetById(planId);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task User_Cannot_Update_AnotherUsersStudyPlanSharingSettings()
    {
        using var scope = new TestScope();
        var ownerId = Guid.NewGuid().ToString();
        var otherId = Guid.NewGuid().ToString();
        await scope.AddUserAsync(ownerId, "owner");
        await scope.AddUserAsync(otherId, "other");
        var planId = await AddStudyPlanAsync(scope, ownerId, "private");
        var controller = CreateStudyPlansController(scope, otherId);

        var result = await controller.SetSharingSettings(planId, new SetCohortSharingRequest { AllowCohortSharing = false });

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task ResourceOwner_Can_Update_StudyPlanSharingSettings()
    {
        using var scope = new TestScope();
        var ownerId = Guid.NewGuid().ToString();
        await scope.AddUserAsync(ownerId, "owner");
        var planId = await AddStudyPlanAsync(scope, ownerId, "private");
        var controller = CreateStudyPlansController(scope, ownerId);

        var result = await controller.SetSharingSettings(planId, new SetCohortSharingRequest { AllowCohortSharing = true });

        Assert.IsType<OkObjectResult>(result);
        var plan = await scope.Db.StudyPlans.FindAsync(planId);
        Assert.Contains("\"allowCohortSharing\":true", plan!.MetadataJson);
    }

    [Fact]
    public async Task User_CannotUpdateAnotherUsersProfile()
    {
        using var scope = new TestScope();
        await scope.AddUserAsync("user-a", "alice");
        await scope.AddUserAsync("user-b", "bob");
        var controller = new UserInformationController(
            scope.UserManager,
            new Azure.Storage.Blobs.BlobServiceClient("UseDevelopmentStorage=true"),
            new AllowAllContentModerationService());
        controller.SetUser("user-a");

        var result = await controller.UpdateUserInformation(new UserUpdateDTO
        {
            SelfIntroduction = "updated by current user only",
            Gender = "Prefer not to say",
            BirthDate = DateTime.UtcNow.Date,
            ShowStudyPlansPublicly = true,
            ShowStudyGroupsPublicly = true
        });

        Assert.IsType<OkObjectResult>(result);
        var userA = await scope.UserManager.FindByIdAsync("user-a");
        var userB = await scope.UserManager.FindByIdAsync("user-b");
        Assert.Equal("updated by current user only", userA!.SelfIntroduction);
        Assert.Null(userB!.SelfIntroduction);
    }

    [Fact]
    public async Task DevelopmentEndpoint_IsUnavailableInProduction()
    {
        using var scope = new TestScope();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(x => x.EnvironmentName).Returns(Environments.Production);
        var controller = new Sciencetopia.Controllers.Admin.AdminToolsController(
            scope.UserManager,
            scope.RoleManager,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdminTools:EnableReset"] = "true",
                ["Admin:ResetSecret"] = "test-secret"
            }).Build(),
            environment.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.Reset(new Sciencetopia.Controllers.Admin.AdminToolsController.ResetAdminRequest(
            "admin@example.test",
            "NewPassword1!"));

        Assert.IsType<NotFoundResult>(result);
    }

    private static StudyPlansController CreateStudyPlansController(TestScope scope, string? userId = null)
    {
        var progress = new Mock<IResourceProgressService>(MockBehavior.Strict);
        progress
            .Setup(x => x.GetPlanProgressByPlanIdsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(new Dictionary<Guid, UserPlanProgressDto>());

        var personalEnrollment = new Mock<IPersonalPlanEnrollmentService>(MockBehavior.Strict);
        var controller = new StudyPlansController(
            scope.Db,
            new PermissionService(scope.Db, new MemoryCache(new MemoryCacheOptions())),
            progress.Object,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<StudyPlansController>.Instance,
            personalEnrollment.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = string.IsNullOrWhiteSpace(userId)
                    ? new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity())
                    : SecurityTestHelpers.Principal(userId)
            }
        };

        return controller;
    }

    private static async Task<Guid> AddStudyPlanAsync(TestScope scope, string ownerId, string privacy)
    {
        var planId = Guid.NewGuid();
        var stableId = Guid.NewGuid();
        var plan = new StudyPlanEntity
        {
            Id = planId,
            StableId = stableId,
            CreatorId = Guid.Parse(ownerId),
            CreatedBy = ownerId,
            Title = "Private plan",
            Description = "security test",
            Status = "Draft",
            IsCurrent = true,
            VersionNumber = 1
        };

        scope.Db.StudyPlans.Add(plan);
        scope.Db.Entry(plan).Property("Privacy").CurrentValue = privacy;
        await scope.Db.SaveChangesAsync();
        return planId;
    }
}
