using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Models;
using Sciencetopia.Models.Enums;
using Sciencetopia.Services;
using System.Security.Claims;

namespace Sciencetopia.Controllers
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

            // Collect visible plan ids from three sources
            var userGuid = Guid.TryParse(userId, out var ug) ? ug : Guid.Empty;

            var ownerIdsQ = _db.StudyPlans
                .Where(p => p.CreatorId == ug)
                .Select(p => p.Id);

            var directIdsQ = _db.StudyPlanUserRoles
                .Where(r => r.UserId == userId)
                .Select(r => r.PlanId);

            var groupIdsQ = _db.StudyGroupUserRoles
                .Where(gr => gr.UserId == userId)
                .Join(_db.StudyGroupStudyPlans, gr => gr.GroupId, gp => gp.StudyGroupId, (gr, gp) => gp.StudyPlanId);

            var planIds = await ownerIdsQ
                .Union(directIdsQ)
                .Union(groupIdsQ)
                .ToListAsync();

            // privacy public fallback
            var publicIds = await _db.StudyPlans
                .Where(p => EF.Property<string>(p, "Privacy") == "public")
                .Select(p => p.Id)
                .ToListAsync();

            planIds = planIds.Union(publicIds).Distinct().ToList();

            var query = _db.StudyPlans.Where(p => planIds.Contains(p.Id));
            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(p => p.Title.Contains(q));
            }

            query = sort switch
            {
                "createdDesc" => query.OrderByDescending(p => p.CreatedDate),
                "createdAsc" => query.OrderBy(p => p.CreatedDate),
                _ => query.OrderByDescending(p => p.UpdatedDate)
            };

            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var result = new List<object>();
            foreach (var p in items)
            {
                var role = await _perm.GetEffectivePlanRoleAsync(userId, p.Id);
                result.Add(new
                {
                    id = p.Id,
                    title = p.Title,
                    description = p.Description,
                    role = role.ToString()
                });
            }

            return Ok(new { total, page, pageSize, items = result });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (!await _perm.CanReadAsync(userId, id)) return Forbid();

            var plan = await _db.StudyPlans.FirstOrDefaultAsync(p => p.Id == id);
            if (plan == null) return NotFound();
            return Ok(new { id = plan.Id, title = plan.Title, description = plan.Description });
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

            var existing = await _db.StudyPlanUserRoles.FirstOrDefaultAsync(r => r.PlanId == id && r.UserId == req.UserId);
            if (existing == null)
            {
                _db.StudyPlanUserRoles.Add(new StudyPlanUserRole { PlanId = id, UserId = req.UserId!, Role = req.Role });
            }
            else
            {
                existing.Role = req.Role;
            }
            await _db.SaveChangesAsync();
            _perm.Invalidate(req.UserId!, id);
            return Ok();
        }

        [HttpDelete("{id}/Share/User/{userId}")]
        public async Task<IActionResult> UnshareFromUser([FromRoute] Guid id, [FromRoute] string userId)
        {
            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(actorId)) return Unauthorized();
            if (!await _perm.CanShareAsync(actorId, id)) return Forbid();

            var existing = await _db.StudyPlanUserRoles.FirstOrDefaultAsync(r => r.PlanId == id && r.UserId == userId);
            if (existing != null)
            {
                _db.StudyPlanUserRoles.Remove(existing);
                await _db.SaveChangesAsync();
                _perm.Invalidate(userId, id);
            }
            return Ok();
        }
    }
}

