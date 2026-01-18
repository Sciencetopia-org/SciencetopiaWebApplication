using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.DTOs;
using Sciencetopia.Models;
using Sciencetopia.Services;

namespace Sciencetopia.Controllers.StudyPlan
{
    [ApiController]
    [Route("api/study-plans")] // canonical lowercase path for new endpoints
    public class StudyPlanVersionsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly StudyPlanVersioningService _versioning;

        public StudyPlanVersionsController(ApplicationDbContext db, StudyPlanVersioningService versioning)
        {
            _db = db;
            _versioning = versioning;
        }

        // POST /study-plans/{stableId}/versions -> Draft from current/base
        [HttpPost("{stableId}/versions")]
        public async Task<IActionResult> CreateDraft([FromRoute] Guid stableId, [FromQuery] Guid? baseVersionId, CancellationToken ct)
        {
            var userId = User?.Identity?.Name ?? string.Empty;
            var draft = await _versioning.CreateDraftAsync(stableId, baseVersionId, userId, ct);
            if (draft == null) return NotFound();
            return Ok(new { versionId = draft.Id, status = draft.Status });
        }

        // PATCH /study-plans/versions/{versionId}
        public class PatchDraftBody
        {
            public Guid[]? Prerequisite { get; set; }
            public Guid[]? MainCurriculum { get; set; }
            public Guid[]? AdvancedTopics { get; set; }
        }

        [HttpPatch("versions/{versionId}")]
        public async Task<IActionResult> PatchDraft([FromRoute] Guid versionId, [FromBody] PatchDraftBody body, CancellationToken ct)
        {
            var outline = new[]
            {
                (StepType: Constants.StudyPlanStepTypes.Prerequisite, LessonStableIds: (body.Prerequisite ?? Array.Empty<Guid>()).AsEnumerable()),
                (StepType: Constants.StudyPlanStepTypes.MainCurriculum, LessonStableIds: (body.MainCurriculum ?? Array.Empty<Guid>()).AsEnumerable()),
                (StepType: Constants.StudyPlanStepTypes.AdvancedTopic, LessonStableIds: (body.AdvancedTopics ?? Array.Empty<Guid>()).AsEnumerable())
            };
            var ok = await _versioning.PatchDraftOutlineAsync(versionId, outline, ct);
            if (!ok) return Conflict(new { code = "not_draft_or_not_found" });
            return Ok(new { versionId });
        }

        // POST /study-plans/versions/{versionId}:publish
        [HttpPost("versions/{versionId}:publish")]
        public async Task<IActionResult> Publish([FromRoute] Guid versionId, CancellationToken ct)
        {
            var actor = User?.Identity?.Name ?? string.Empty;
            var res = await _versioning.PublishAsync(versionId, actor, ct);
            if (!res.ok)
            {
                if (res.error == "missing_lessons") return BadRequest(new { code = res.error });
                return Conflict(new { code = res.error });
            }
            return Ok(new { versionId = res.published!.Id, status = res.published!.Status });
        }

        // GET /study-plans/{stableId} -> canonical resolves current
        [HttpGet("{stableId}")]
        public async Task<IActionResult> GetCanonical([FromRoute] Guid stableId, CancellationToken ct)
        {
            var cur = await _db.StudyPlans.AsNoTracking().Where(p => p.StableId == stableId && (p.IsCurrent || p.Status == "Current")).OrderByDescending(p => p.VersionNumber).FirstOrDefaultAsync(ct);
            if (cur == null) return NotFound();
            return Ok(new { id = cur.Id, stableId = cur.StableId, versionNumber = cur.VersionNumber, status = cur.Status, lockfile = cur.LockfileJson });
        }

        // GET /study-plans/{stableId}/versions/{versionId}
        [HttpGet("{stableId}/versions/{versionId}")]
        public async Task<IActionResult> GetVersion([FromRoute] Guid stableId, [FromRoute] Guid versionId, CancellationToken ct)
        {
            var ver = await _db.StudyPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == versionId && p.StableId == stableId, ct);
            if (ver == null) return NotFound();
            return Ok(new { id = ver.Id, stableId = ver.StableId, versionNumber = ver.VersionNumber, status = ver.Status, lockfile = ver.LockfileJson });
        }

        // GET /study-plans/{stableId}/versions/{from}/diff/{to}
        [HttpGet("{stableId}/versions/{from}/diff/{to}")]
        public async Task<IActionResult> Diff([FromRoute] Guid stableId, [FromRoute] Guid from, [FromRoute] Guid to, CancellationToken ct)
        {
            var v1 = await _db.StudyPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == from && p.StableId == stableId, ct);
            var v2 = await _db.StudyPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == to && p.StableId == stableId, ct);
            if (v1 == null || v2 == null) return NotFound();
            var lf1 = StudyPlanVersioningService.ParseLockfile(v1.LockfileJson);
            var lf2 = StudyPlanVersioningService.ParseLockfile(v2.LockfileJson);
            var diff = _versioning.Diff(lf1, lf2);
            return Ok(diff);
        }
    }
}

