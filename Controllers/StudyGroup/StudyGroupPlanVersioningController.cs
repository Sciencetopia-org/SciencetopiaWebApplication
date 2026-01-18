using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.DTOs;
using Sciencetopia.Services;

namespace Sciencetopia.Controllers.StudyGroups
{
    [ApiController]
    [Route("api/study-groups/{id}")]
    public class StudyGroupPlanVersioningController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly StudyPlanVersioningService _versioning;

        public StudyGroupPlanVersioningController(ApplicationDbContext db, StudyPlanVersioningService versioning)
        {
            _db = db;
            _versioning = versioning;
        }

        // GET /study-groups/{id}/head-status
        [HttpGet("head-status")]
        public async Task<IActionResult> HeadStatus([FromRoute] Guid id, CancellationToken ct)
        {
            var mapping = await _db.StudyGroupStudyPlans.AsNoTracking().FirstOrDefaultAsync(x => x.StudyGroupId == id, ct);
            if (mapping == null) return NotFound();
            var stableId = mapping.StudyPlanStableId;
            var current = await _db.StudyPlans.AsNoTracking().Where(p => p.StableId == stableId && (p.IsCurrent || p.Status == "Current")).OrderByDescending(p => p.VersionNumber).FirstOrDefaultAsync(ct);
            if (current == null) return NotFound();
            var pinned = mapping.ResolveToHead
                ? current
                : await _db.StudyPlans.AsNoTracking().Where(p => p.StableId == stableId && p.VersionNumber == (mapping.PinnedVersionNumber ?? current.VersionNumber)).FirstOrDefaultAsync(ct);
            if (pinned == null) pinned = current;

            var hasNewer = current.VersionNumber > pinned.VersionNumber;
            LockfileDiff? diff = null;
            if (hasNewer)
            {
                var lf1 = StudyPlanVersioningService.ParseLockfile(pinned.LockfileJson);
                var lf2 = StudyPlanVersioningService.ParseLockfile(current.LockfileJson);
                diff = _versioning.Diff(lf1, lf2);
            }

            return Ok(new { hasNewerVersion = hasNewer, fromVersion = pinned.VersionNumber, toVersion = current.VersionNumber, diff });
        }

        // GET /study-groups/{id}/rollforward:preview?toVersionId=...
        [HttpGet("rollforward:preview")]
        public async Task<IActionResult> RollforwardPreview([FromRoute] Guid id, [FromQuery] Guid toVersionId, CancellationToken ct)
        {
            var mapping = await _db.StudyGroupStudyPlans.AsNoTracking().FirstOrDefaultAsync(x => x.StudyGroupId == id, ct);
            if (mapping == null) return NotFound();
            var to = await _db.StudyPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == toVersionId && p.StableId == mapping.StudyPlanStableId, ct);
            if (to == null) return NotFound();
            var from = mapping.ResolveToHead
                ? await _db.StudyPlans.AsNoTracking().FirstOrDefaultAsync(p => p.StableId == mapping.StudyPlanStableId && (p.IsCurrent || p.Status == "Current"), ct)
                : await _db.StudyPlans.AsNoTracking().FirstOrDefaultAsync(p => p.StableId == mapping.StudyPlanStableId && p.VersionNumber == (mapping.PinnedVersionNumber ?? to.VersionNumber), ct);
            if (from == null) return NotFound();
            var diff = _versioning.Diff(StudyPlanVersioningService.ParseLockfile(from.LockfileJson), StudyPlanVersioningService.ParseLockfile(to.LockfileJson));
            return Ok(diff);
        }

        public class RollRequest { public Guid ToVersionId { get; set; } public DateTimeOffset? EffectiveAt { get; set; } public string? Reason { get; set; } }
        // POST /study-groups/{id}/study-plan:rollforward
        [HttpPost("study-plan:rollforward")]
        public async Task<IActionResult> Rollforward([FromRoute] Guid id, [FromBody] RollRequest req, CancellationToken ct)
        {
            var mapping = await _db.StudyGroupStudyPlans.FirstOrDefaultAsync(x => x.StudyGroupId == id, ct);
            if (mapping == null) return NotFound();
            var to = await _db.StudyPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.ToVersionId && p.StableId == mapping.StudyPlanStableId, ct);
            if (to == null) return NotFound();
            var now = DateTimeOffset.UtcNow;
            if (req.EffectiveAt.HasValue && req.EffectiveAt.Value > now)
            {
                // schedule
                _db.GroupPlanSwitches.Add(new Models.GroupPlanSwitch
                {
                    StudyGroupId = id,
                    PlanStableId = mapping.StudyPlanStableId,
                    FromVersionId = mapping.PlanVersionId,
                    FromVersionNumber = mapping.PinnedVersionNumber,
                    ToVersionId = to.Id,
                    ToVersionNumber = to.VersionNumber,
                    EffectiveAt = req.EffectiveAt.Value,
                    Actor = User?.Identity?.Name,
                    Reason = req.Reason
                });
                await _db.SaveChangesAsync(ct);
                return Ok(new { ok = true, scheduled = true, effectiveAt = req.EffectiveAt.Value });
            }
            else
            {
                // immediate apply
                var log = new Models.GroupPlanSwitch
                {
                    StudyGroupId = id,
                    PlanStableId = mapping.StudyPlanStableId,
                    FromVersionId = mapping.PlanVersionId,
                    FromVersionNumber = mapping.PinnedVersionNumber,
                    ToVersionId = to.Id,
                    ToVersionNumber = to.VersionNumber,
                    EffectiveAt = now,
                    ExecutedAt = now,
                    Actor = User?.Identity?.Name,
                    Reason = req.Reason
                };
                mapping.PinnedVersionNumber = to.VersionNumber;
                mapping.PlanVersionId = to.Id;
                mapping.UpdatedDate = DateTime.UtcNow;
                _db.GroupPlanSwitches.Add(log);
                await _db.SaveChangesAsync(ct);
                return Ok(new { ok = true, toVersionNumber = to.VersionNumber });
            }
        }

        public class RollbackRequest { public Guid ToVersionId { get; set; } public string? Reason { get; set; } }
        // POST /study-groups/{id}/study-plan:rollback
        [HttpPost("study-plan:rollback")]
        public async Task<IActionResult> Rollback([FromRoute] Guid id, [FromBody] RollbackRequest req, CancellationToken ct)
        {
            var mapping = await _db.StudyGroupStudyPlans.FirstOrDefaultAsync(x => x.StudyGroupId == id, ct);
            if (mapping == null) return NotFound();
            var to = await _db.StudyPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.ToVersionId && p.StableId == mapping.StudyPlanStableId, ct);
            if (to == null) return NotFound();
            var now = DateTimeOffset.UtcNow;
            var log = new Models.GroupPlanSwitch
            {
                StudyGroupId = id,
                PlanStableId = mapping.StudyPlanStableId,
                FromVersionId = mapping.PlanVersionId,
                FromVersionNumber = mapping.PinnedVersionNumber,
                ToVersionId = to.Id,
                ToVersionNumber = to.VersionNumber,
                EffectiveAt = now,
                ExecutedAt = now,
                Actor = User?.Identity?.Name,
                Reason = req.Reason
            };
            mapping.PinnedVersionNumber = to.VersionNumber;
            mapping.PlanVersionId = to.Id;
            mapping.UpdatedDate = DateTime.UtcNow;
            _db.GroupPlanSwitches.Add(log);
            await _db.SaveChangesAsync(ct);
            return Ok(new { ok = true, toVersionNumber = to.VersionNumber });
        }
    }
}
