using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sciencetopia.Data;
using Sciencetopia.Models;
using Sciencetopia.Models.Enums;

namespace Sciencetopia.Services;

public class PermissionService
{
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    public PermissionService(ApplicationDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<PlanRole> GetEffectivePlanRoleAsync(string userId, Guid planId, CancellationToken ct = default)
    {
        var cacheKey = $"perm:plan:{planId}:user:{userId}";
        if (_cache.TryGetValue(cacheKey, out PlanRole cached))
            return cached;

        // Owner via StudyPlans.CreatorId (GUID stored)
        var userGuid = Guid.TryParse(userId, out var ug) ? ug : Guid.Empty;

        var plan = await _db.StudyPlans
            .Where(p => p.Id == planId)
            .Select(p => new { p.Id, p.Title, p.Description, p.CreatorId, Privacy = (string?)EF.Property<string>(p, "Privacy") })
            .FirstOrDefaultAsync(ct);

        if (plan == null)
        {
            return PlanRole.Viewer; // non-existing plan → minimal
        }

        // a) Owner
        if (plan.CreatorId == userGuid)
        {
            return SetCache(cacheKey, PlanRole.Owner);
        }

        PlanRole best = PlanRole.Viewer;

        // b) Direct user role
        var direct = await _db.StudyPlanUserRoles
            .Where(r => r.PlanId == planId && r.UserId == userId)
            .Select(r => r.Role)
            .FirstOrDefaultAsync(ct);
        if (direct > best) best = direct;

        // c) Group visibility: user is member of groups with roles; plan linked to those groups
        var hasGroupLink = await _db.StudyGroupUserRoles
            .Where(gr => gr.UserId == userId)
            .Join(_db.StudyGroupStudyPlans, gr => gr.GroupId, gp => gp.StudyGroupId, (gr, gp) => new { gr, gp })
            .AnyAsync(x => x.gp.StudyPlanId == planId, ct);
        if (hasGroupLink && best < PlanRole.Viewer)
            best = PlanRole.Viewer; // default visibility level from group

        // d) Privacy fallback (Public → Viewer)
        var privacy = plan.Privacy?.ToLowerInvariant();
        if (privacy == "public" && best < PlanRole.Viewer)
            best = PlanRole.Viewer;

        return SetCache(cacheKey, best);
    }

    private PlanRole SetCache(string key, PlanRole role)
    {
        _cache.Set(key, role, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(90)
        });
        return role;
    }

    public Task<bool> CanReadAsync(string userId, Guid planId, CancellationToken ct = default)
        => CheckAsync(userId, planId, r => r >= PlanRole.Viewer, ct);

    public Task<bool> CanCommentAsync(string userId, Guid planId, CancellationToken ct = default)
        => CheckAsync(userId, planId, r => r >= PlanRole.Commenter, ct);

    public Task<bool> CanEditAsync(string userId, Guid planId, CancellationToken ct = default)
        => CheckAsync(userId, planId, r => r >= PlanRole.Editor || r == PlanRole.Owner, ct);

    public Task<bool> CanShareAsync(string userId, Guid planId, CancellationToken ct = default)
        => CheckAsync(userId, planId, r => r == PlanRole.Owner, ct);

    private async Task<bool> CheckAsync(string userId, Guid planId, Func<PlanRole, bool> predicate, CancellationToken ct)
    {
        var role = await GetEffectivePlanRoleAsync(userId, planId, ct);
        return predicate(role);
    }

    public void Invalidate(string userId, Guid planId)
    {
        var cacheKey = $"perm:plan:{planId}:user:{userId}";
        _cache.Remove(cacheKey);
    }
}

