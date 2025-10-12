using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Models;
using Sciencetopia.Models.Enums;
using Sciencetopia.Services;
using System.Security.Claims;

namespace Sciencetopia.Controllers.StudyPlan
{
    [ApiController]
    [Route("api/StudyPlans")] // PascalCase
    public class StudyPlansController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly PermissionService _perm;

        public StudyPlansController(ApplicationDbContext db, PermissionService perm)
        {
            _db = db;
            _perm = perm;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? q = null, [FromQuery] string? sort = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var ug = Guid.TryParse(userId, out var parsed) ? parsed : Guid.Empty;

            // Single query: compute visibility in DB, avoid description, no tracking
            var baseQ = _db.StudyPlans.AsNoTracking().Where(p =>
                p.CreatorId == ug
                || _db.StudyPlanUserRoles.Any(r => r.PlanStableId == p.StableId && r.UserId == userId)
                || _db.StudyGroupStudyPlans.Any(gp => gp.StudyPlanStableId == p.StableId &&
                       _db.StudyGroupUserRoles.Any(gr => gr.GroupId == gp.StudyGroupId && gr.UserId == userId))
                || EF.Property<string>(p, "Privacy") == "public"
            );

            if (!string.IsNullOrWhiteSpace(q))
            {
                baseQ = baseQ.Where(p => p.Title.Contains(q));
            }

            baseQ = sort switch
            {
                "createdDesc" => baseQ.OrderByDescending(p => p.CreatedDate),
                "createdAsc" => baseQ.OrderBy(p => p.CreatedDate),
                _ => baseQ.OrderByDescending(p => p.UpdatedDate)
            };

            var total = await baseQ.CountAsync();

            // Project only Id and Title for speed
            var slice = await baseQ
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new
                {
                    p.Id,
                    p.StableId,
                    p.VersionNumber,
                    p.IsCurrent,
                    p.Status,
                    p.Title,
                    p.Description,
                    p.CreatedDate,
                    p.UpdatedDate
                })
                .ToListAsync();

            var stableIds = slice.Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId).Distinct().ToList();

            var aggregates = await _db.StudyPlans.AsNoTracking()
                .Where(p => stableIds.Contains(p.StableId == Guid.Empty ? p.Id : p.StableId))
                .GroupBy(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
                .Select(g => new
                {
                    StableId = g.Key,
                    LatestVersionNumber = g.Max(p => p.VersionNumber),
                    CurrentVersionNumber = g.Where(p => p.IsCurrent).Select(p => (int?)p.VersionNumber).FirstOrDefault()
                })
                .ToListAsync();

            var aggregatesDict = aggregates.ToDictionary(a => a.StableId, a => a);

            var items = await Task.WhenAll(slice.Select(async p =>
            {
                var stableId = p.StableId == Guid.Empty ? p.Id : p.StableId;
                var agg = aggregatesDict.TryGetValue(stableId, out var entry)
                    ? entry
                    : new { StableId = stableId, LatestVersionNumber = p.VersionNumber, CurrentVersionNumber = (int?) (p.IsCurrent ? p.VersionNumber : (int?)null) };

                var currentVersionNumber = agg.CurrentVersionNumber ?? agg.LatestVersionNumber;
                var hasUpgrade = !p.IsCurrent && p.VersionNumber < agg.LatestVersionNumber;

                var role = (await _perm.GetEffectivePlanRoleAsync(userId, p.Id)).ToString();

                return new
                {
                    id = p.Id,
                    stableId,
                    versionNumber = p.VersionNumber,
                    latestVersionNumber = agg.LatestVersionNumber,
                    currentVersionNumber,
                    isCurrent = p.IsCurrent,
                    status = p.Status,
                    title = p.Title,
                    description = p.Description,
                    updatedAt = p.UpdatedDate,
                    createdAt = p.CreatedDate,
                    hasUpgrade,
                    role
                };
            }));

            return Ok(new { total, page, pageSize, items });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (!await _perm.CanReadAsync(userId, id)) return Forbid();

            var plan = await _db.StudyPlans.FirstOrDefaultAsync(p => p.Id == id);
            if (plan == null) return NotFound();

            var stableId = plan.StableId == Guid.Empty ? plan.Id : plan.StableId;

            var current = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.StableId == stableId)
                .OrderByDescending(p => p.IsCurrent)
                .ThenByDescending(p => p.VersionNumber)
                .Select(p => new { p.Id, p.VersionNumber, p.IsCurrent })
                .FirstOrDefaultAsync();

            var latestVersionNumber = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.StableId == stableId)
                .MaxAsync(p => (int?)p.VersionNumber) ?? plan.VersionNumber;

            return Ok(new
            {
                id = plan.Id,
                stableId,
                versionNumber = plan.VersionNumber,
                isCurrent = plan.IsCurrent,
                currentVersionId = current?.Id,
                currentVersionNumber = current?.VersionNumber,
                latestVersionNumber,
                title = plan.Title,
                description = plan.Description
            });
        }

        // B4-2: GET /StudyPlans/{id}/enrollment/me
        [HttpGet("{id}/Enrollment/Me")]
        public async Task<IActionResult> GetEnrollmentMe([FromRoute] Guid id, [FromServices] Sciencetopia.Services.Cohorts.ICohortService cohortService)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (!await _perm.CanReadAsync(userId, id)) return Forbid();

            var dto = await cohortService.GetEnrollmentForUserAsync(id, userId, HttpContext.RequestAborted);
            return Ok(dto);
        }

        // B4-5: GET /StudyPlans/{id}/joinable-cohorts
        [HttpGet("{id}/JoinableCohorts")]
        public async Task<IActionResult> GetJoinableCohorts([FromRoute] Guid id, [FromServices] Neo4j.Driver.IDriver driver)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (!await _perm.CanReadAsync(userId, id)) return Forbid();

            var stableId = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
                .FirstOrDefaultAsync();
            if (stableId == Guid.Empty) return NotFound();

            var cohorts = await _db.Cohorts.AsNoTracking()
                .Where(c => c.StudyPlanStableId == stableId)
                .Select(c => new { c.Id, c.Title, c.StudyGroupId, c.Visibility })
                .ToListAsync();

            bool alreadyActive = false;
            await using (var session = driver.AsyncSession())
            {
                alreadyActive = await session.ExecuteReadAsync(async tx =>
                {
                    var cur = await tx.RunAsync("MATCH (u:User {id:$userId})-[:ENROLLED_IN]->(:PlanVersion {studyPlanId:$planId}) RETURN true LIMIT 1", new { planId = stableId.ToString(), userId });
                    return await cur.PeekAsync() != null;
                });
            }

            var groupScoped = new List<object>();
            var pub = new List<object>();

            foreach (var c in cohorts)
            {
                var reason = alreadyActive ? "alreadyInPlan" : null;
                var canJoin = !alreadyActive;
                if (c.StudyGroupId.HasValue)
                {
                    var isMember = await _db.StudyGroupUserRoles.AsNoTracking().AnyAsync(gr => gr.GroupId == c.StudyGroupId && gr.UserId == userId);
                    if (!isMember) { canJoin = false; reason = reason ?? "notGroupMember"; }
                    groupScoped.Add(new { id = c.Id, title = c.Title, canJoin, reason });
                }
                else if (string.Equals(c.Visibility, "public", StringComparison.OrdinalIgnoreCase))
                {
                    pub.Add(new { id = c.Id, title = c.Title, canJoin, reason });
                }
            }

            return Ok(new { groupScoped, @public = pub, soloAvailable = !alreadyActive });
        }

        [HttpGet("{id}/Permissions/Effective")]
        public async Task<IActionResult> GetEffectivePermission([FromRoute] Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            var role = await _perm.GetEffectivePlanRoleAsync(userId, id);
            return Ok(new { role = role.ToString() });
        }

        public class ShareUserRequest { public string? UserId { get; set; } public PlanRole Role { get; set; } }

        [HttpPost("{id}/Share/User")]
        public async Task<IActionResult> ShareToUser([FromRoute] Guid id, [FromBody] ShareUserRequest req)
        {
            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(actorId)) return Unauthorized();
            if (!await _perm.CanShareAsync(actorId, id)) return Forbid();
            if (string.IsNullOrEmpty(req.UserId)) return BadRequest("UserId required");

            var stableId = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
                .FirstOrDefaultAsync();
            if (stableId == Guid.Empty) return NotFound();

            var existing = await _db.StudyPlanUserRoles.FirstOrDefaultAsync(r => r.PlanStableId == stableId && r.UserId == req.UserId);
            if (existing == null)
            {
                _db.StudyPlanUserRoles.Add(new StudyPlanUserRole { PlanStableId = stableId, UserId = req.UserId!, Role = req.Role });
            }
            else
            {
                existing.Role = req.Role;
            }
            await _db.SaveChangesAsync();
            await _perm.InvalidateAsync(req.UserId!, id, HttpContext.RequestAborted);
            return Ok();
        }

        [HttpDelete("{id}/Share/User/{userId}")]
        public async Task<IActionResult> UnshareFromUser([FromRoute] Guid id, [FromRoute] string userId)
        {
            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(actorId)) return Unauthorized();
            if (!await _perm.CanShareAsync(actorId, id)) return Forbid();

            var stableId = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
                .FirstOrDefaultAsync();
            if (stableId == Guid.Empty) return NotFound();

            var existing = await _db.StudyPlanUserRoles.FirstOrDefaultAsync(r => r.PlanStableId == stableId && r.UserId == userId);
            if (existing != null)
            {
                _db.StudyPlanUserRoles.Remove(existing);
                await _db.SaveChangesAsync();
                await _perm.InvalidateAsync(userId, id, HttpContext.RequestAborted);
            }
            return Ok();
        }
    }
}
