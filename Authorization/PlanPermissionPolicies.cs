using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Services;

namespace Sciencetopia.Authorization;

public enum PlanPermissionAction
{
    PlanEdit,
    PlanPublish,
    CohortManage,
    CohortInvite
}

public class PlanPermissionRequirement : IAuthorizationRequirement
{
    public PlanPermissionAction Action { get; }
    public PlanPermissionRequirement(PlanPermissionAction action) => Action = action;
}

public class PlanPermissionHandler : AuthorizationHandler<PlanPermissionRequirement>
{
    private readonly PermissionService _perm;
    private readonly IHttpContextAccessor _http;
    private readonly ApplicationDbContext _db;

    public PlanPermissionHandler(PermissionService perm, IHttpContextAccessor http, ApplicationDbContext db)
    {
        _perm = perm;
        _http = http;
        _db = db;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PlanPermissionRequirement requirement)
    {
        var http = _http.HttpContext;
        if (http == null)
        {
            return;
        }

        var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return; // unauthenticated
        }

        // Try read identifiers from route first, then query
        Guid planId = default;
        Guid? cohortId = null;

        var routeValues = http.Request.RouteValues;
        if (routeValues.TryGetValue("planId", out var pVal) && Guid.TryParse(pVal?.ToString(), out var pGuid))
        {
            planId = pGuid;
        }
        if (routeValues.TryGetValue("cohortId", out var cVal) && Guid.TryParse(cVal?.ToString(), out var cGuid))
        {
            cohortId = cGuid;
        }

        if (planId == default)
        {
            // fallback to query
            if (Guid.TryParse(http.Request.Query["planId"], out var p2))
                planId = p2;
            if (Guid.TryParse(http.Request.Query["cohortId"], out var c2))
                cohortId = c2;
        }

        // If still no planId but we have cohortId, resolve via SQL
        if (planId == default && cohortId.HasValue)
        {
            planId = await _db.Cohorts.AsNoTracking()
                .Where(c => c.Id == cohortId.Value)
                .Select(c => c.StudyPlanStableId)
                .FirstOrDefaultAsync();
        }

        if (planId == default)
        {
            return; // missing context
        }

        var eff = await _perm.GetEffectivePermissionsAsync(userId, planId, cohortId, http.RequestAborted);

        var ok = requirement.Action switch
        {
            PlanPermissionAction.PlanEdit => eff.CanEdit,
            PlanPermissionAction.PlanPublish => eff.CanPublish,
            PlanPermissionAction.CohortManage => eff.CohortManage,
            PlanPermissionAction.CohortInvite => eff.CohortInvite,
            _ => false
        };

        if (ok)
        {
            context.Succeed(requirement);
        }
    }
}
