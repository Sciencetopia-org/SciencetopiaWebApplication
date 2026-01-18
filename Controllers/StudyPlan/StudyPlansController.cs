using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Models;
using Sciencetopia.Models.Enums;
using Sciencetopia.Services;
using Neo4j.Driver;
using System.Security.Claims;

namespace Sciencetopia.Controllers.StudyPlan
{
    [ApiController]
    [Route("api/StudyPlans")] // PascalCase
    public class StudyPlansController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly PermissionService _perm;
        private readonly Neo4j.Driver.IDriver _driver;

        public StudyPlansController(ApplicationDbContext db, PermissionService perm, Neo4j.Driver.IDriver driver)
        {
            _db = db;
            _perm = perm;
            _driver = driver;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? q = null, [FromQuery] string? sort = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var ug = Guid.TryParse(userId, out var parsed) ? parsed : Guid.Empty;

            // 1) Gather plan stableIds from Neo4j relations (created, member group shares, enrolled)
            var planIdsFromGraph = new HashSet<Guid>();
            await using (var session = _driver.AsyncSession())
            {
                // created
                var createdCur = await session.RunAsync("MATCH (u:User {id:$uid})-[:CREATED]->(p:StudyPlan) RETURN DISTINCT p.id AS id", new { uid = userId });
                while (await createdCur.FetchAsync())
                {
                    var idStr = createdCur.Current["id"].As<string>();
                    if (!string.IsNullOrWhiteSpace(idStr) && Guid.TryParse(idStr, out var gid)) planIdsFromGraph.Add(gid);
                }

                // group shares by membership
                var shareCur = await session.RunAsync("MATCH (u:User {id:$uid})-[:MEMBER_OF]->(:StudyGroup)-[:SHARES_PLAN]->(p:StudyPlan) RETURN DISTINCT p.id AS id", new { uid = userId });
                while (await shareCur.FetchAsync())
                {
                    var idStr = shareCur.Current["id"].As<string>();
                    if (!string.IsNullOrWhiteSpace(idStr) && Guid.TryParse(idStr, out var gid)) planIdsFromGraph.Add(gid);
                }

                // enrollments (handle both historical PlanVersion and current StudyPlan models)
                var enrollPvCur = await session.RunAsync("MATCH (u:User {id:$uid})-[:ENROLLED_IN]->(pv:PlanVersion) RETURN DISTINCT pv.studyPlanId AS id", new { uid = userId });
                while (await enrollPvCur.FetchAsync())
                {
                    var idStr = enrollPvCur.Current["id"].As<string>();
                    if (!string.IsNullOrWhiteSpace(idStr) && Guid.TryParse(idStr, out var gid)) planIdsFromGraph.Add(gid);
                }

                var enrollPlanCur = await session.RunAsync("MATCH (u:User {id:$uid})-[:ENROLLED_IN]->(p:StudyPlan) RETURN DISTINCT p.id AS id", new { uid = userId });
                while (await enrollPlanCur.FetchAsync())
                {
                    var idStr = enrollPlanCur.Current["id"].As<string>();
                    if (!string.IsNullOrWhiteSpace(idStr) && Guid.TryParse(idStr, out var gid)) planIdsFromGraph.Add(gid);
                }
            }

            var stableSet = planIdsFromGraph.ToList();

            Console.WriteLine($"[StudyPlansController.List] User {userId} has {stableSet.Count} plan stableIds from graph");

            // 2) Combine with SQL-based visibility: owner (legacy CreatedBy/CreatorId), direct user roles, group links, public
            var baseQ = _db.StudyPlans.AsNoTracking().Where(p =>
                // Graph visibility by normalized stable id
                stableSet.Contains(p.StableId == Guid.Empty ? p.Id : p.StableId)
                // Also include creator matches for legacy rows
                || ((ug != Guid.Empty && p.CreatorId == ug) || p.CreatedBy == userId)
                // Direct user role on normalized stable id
                || _db.StudyPlanUserRoles.Any(r => r.PlanStableId == (p.StableId == Guid.Empty ? p.Id : p.StableId) && r.UserId == userId)
                // Group link (user has a role in the group that shares this plan)
                || _db.StudyGroupStudyPlans.Any(gp => gp.StudyPlanStableId == (p.StableId == Guid.Empty ? p.Id : p.StableId)
                    && _db.UserGroups.Any(gr => gr.GroupId == gp.StudyGroupId && gr.UserId == userId))
                // public plans (case-insensitive)
                || (EF.Property<string>(p, "Privacy") != null && (EF.Property<string>(p, "Privacy").ToLower() == "public"))
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
                .Join(_db.CohortOfferings.AsNoTracking(),
                    c => c.CurrentOfferingId,
                    o => o.Id,
                    (c, o) => new { Cohort = c, Offering = o })
                .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                    co => co.Offering.StudyGroupStudyPlanId,
                    sgsp => sgsp.Id,
                    (co, sgsp) => new
                    {
                        co.Cohort.Id,
                        co.Cohort.Title,
                        co.Cohort.StudyGroupId,
                        co.Cohort.Visibility,
                        sgsp.StudyPlanStableId
                    })
                .Where(x => x.StudyPlanStableId == stableId)
                .Select(x => new { x.Id, x.Title, x.StudyGroupId, x.Visibility })
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
                if (c.StudyGroupId.HasValue && c.StudyGroupId.Value != Guid.Empty)
                {
                    var isMember = await _db.UserGroups.AsNoTracking().AnyAsync(gr => gr.GroupId == c.StudyGroupId.Value && gr.UserId == userId);
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
