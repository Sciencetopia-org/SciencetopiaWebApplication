using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.DTOs;
using Sciencetopia.Models;

namespace Sciencetopia.Services
{
    public class StudyPlanVersioningService
    {
        private readonly ApplicationDbContext _db;

        public StudyPlanVersioningService(ApplicationDbContext db)
        {
            _db = db;
        }

        // Create Draft from current or a specified version
        public async Task<StudyPlanEntity?> CreateDraftAsync(Guid planStableId, Guid? baseVersionId, string actor, CancellationToken ct)
        {
            var baseVersion = baseVersionId.HasValue
                ? await _db.StudyPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == baseVersionId.Value, ct)
                : await _db.StudyPlans.AsNoTracking().Where(p => p.StableId == planStableId && p.IsCurrent).FirstOrDefaultAsync(ct);

            if (baseVersion == null)
            {
                return null;
            }

            var nextVersionNumber = await _db.StudyPlans.AsNoTracking().Where(p => p.StableId == planStableId).Select(p => (int?)p.VersionNumber).MaxAsync(ct) ?? baseVersion.VersionNumber;
            nextVersionNumber += 1;

            var draft = new StudyPlanEntity
            {
                Id = Guid.NewGuid(),
                StableId = planStableId,
                VersionNumber = nextVersionNumber,
                Status = "Draft",
                IsCurrent = false,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = actor,
                Title = baseVersion.Title,
                Description = baseVersion.Description,
                CreatorId = baseVersion.CreatorId,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow,
                LockfileJson = null
            };

            await _db.StudyPlans.AddAsync(draft, ct);
            await _db.SaveChangesAsync(ct);

            // Copy outline as snapshots with unresolved versionNumber=0
            var baseSnapshots = await _db.StudyPlanLessonSnapshots
                .AsNoTracking()
                .Where(s => s.StudyPlanStableId == planStableId && s.StudyPlanVersionNumber == baseVersion.VersionNumber)
                .OrderBy(s => s.StepOrder)
                .ToListAsync(ct);

            if (baseSnapshots.Count > 0)
            {
                var drafts = baseSnapshots.Select(s => new StudyPlanLessonSnapshot
                {
                    StudyPlanStableId = planStableId,
                    StudyPlanVersionNumber = draft.VersionNumber,
                    LessonStableId = s.LessonStableId,
                    LessonVersionNumber = 0, // unresolved in draft
                    StepType = s.StepType,
                    StepOrder = s.StepOrder
                }).ToList();
                await _db.StudyPlanLessonSnapshots.AddRangeAsync(drafts, ct);
                await _db.SaveChangesAsync(ct);
            }

            return draft;
        }

        // Patch draft outline: replace outline by stableId list per section
        public async Task<bool> PatchDraftOutlineAsync(Guid draftVersionId, IEnumerable<(string StepType, IEnumerable<Guid> LessonStableIds)> outline, CancellationToken ct)
        {
            var draft = await _db.StudyPlans.FirstOrDefaultAsync(p => p.Id == draftVersionId, ct);
            if (draft == null || !string.Equals(draft.Status, "Draft", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var snapshots = new List<StudyPlanLessonSnapshot>();
            int order = 0;
            foreach (var section in outline)
            {
                foreach (var ls in section.LessonStableIds ?? Enumerable.Empty<Guid>())
                {
                    order++;
                    snapshots.Add(new StudyPlanLessonSnapshot
                    {
                        StudyPlanStableId = draft.StableId,
                        StudyPlanVersionNumber = draft.VersionNumber,
                        LessonStableId = ls,
                        LessonVersionNumber = 0,
                        StepType = section.StepType,
                        StepOrder = order
                    });
                }
            }

            var existing = _db.StudyPlanLessonSnapshots.Where(s => s.StudyPlanStableId == draft.StableId && s.StudyPlanVersionNumber == draft.VersionNumber);
            _db.StudyPlanLessonSnapshots.RemoveRange(existing);
            await _db.SaveChangesAsync(ct);
            if (snapshots.Count > 0)
            {
                await _db.StudyPlanLessonSnapshots.AddRangeAsync(snapshots, ct);
            }
            await _db.SaveChangesAsync(ct);
            return true;
        }

        // Publish a draft: resolve current lesson versions, build lockfile, set Current, archive old current
        public async Task<(bool ok, string? error, StudyPlanEntity? published)> PublishAsync(Guid draftVersionId, string actor, CancellationToken ct)
        {
            var draft = await _db.StudyPlans.FirstOrDefaultAsync(p => p.Id == draftVersionId, ct);
            if (draft == null)
                return (false, "not_found", null);
            if (!string.Equals(draft.Status, "Draft", StringComparison.OrdinalIgnoreCase))
                return (false, "not_draft", null);

            var outline = await _db.StudyPlanLessonSnapshots
                .Where(s => s.StudyPlanStableId == draft.StableId && s.StudyPlanVersionNumber == draft.VersionNumber)
                .OrderBy(s => s.StepOrder)
                .ToListAsync(ct);

            // resolve current lesson versions
            var missing = new List<Guid>();
            var resolved = new List<(Guid LessonStableId, int LessonVersionNumber, string StepType, int Index)>();
            int idx = 0;
            foreach (var s in outline)
            {
                idx++;
                var current = await _db.Lessons.AsNoTracking()
                    .Where(l => l.StableId == s.LessonStableId && l.IsCurrent)
                    .OrderByDescending(l => l.VersionNumber)
                    .FirstOrDefaultAsync(ct);
                if (current == null)
                {
                    missing.Add(s.LessonStableId);
                }
                else
                {
                    resolved.Add((s.LessonStableId, current.VersionNumber, s.StepType, idx));
                }
            }
            if (missing.Count > 0)
            {
                return (false, "missing_lessons", null);
            }

            // replace snapshots with resolved versions
            var existing = _db.StudyPlanLessonSnapshots.Where(s => s.StudyPlanStableId == draft.StableId && s.StudyPlanVersionNumber == draft.VersionNumber);
            _db.StudyPlanLessonSnapshots.RemoveRange(existing);
            await _db.SaveChangesAsync(ct);
            await _db.StudyPlanLessonSnapshots.AddRangeAsync(resolved.Select(r => new StudyPlanLessonSnapshot
            {
                StudyPlanStableId = draft.StableId,
                StudyPlanVersionNumber = draft.VersionNumber,
                LessonStableId = r.LessonStableId,
                LessonVersionNumber = r.LessonVersionNumber,
                StepType = r.StepType,
                StepOrder = r.Index
            }), ct);
            await _db.SaveChangesAsync(ct);

            // build lockfile
            var lockfile = new StudyPlanLockfile
            {
                Lessons = resolved.Select(r => new StudyPlanLockfileItem
                {
                    LessonStableId = r.LessonStableId,
                    LessonVersionNumber = r.LessonVersionNumber,
                    StepType = r.StepType,
                    Index = r.Index
                }).ToList()
            };
            draft.LockfileJson = JsonSerializer.Serialize(lockfile);
            draft.Status = "Current";
            draft.IsCurrent = true;
            draft.PublishedAt = DateTimeOffset.UtcNow;
            draft.ApprovedAt = draft.PublishedAt;
            draft.ApprovedBy = actor;
            draft.UpdatedDate = DateTime.UtcNow;

            // archive old current
            var currents = await _db.StudyPlans.Where(p => p.StableId == draft.StableId && p.IsCurrent && p.Id != draft.Id).ToListAsync(ct);
            foreach (var c in currents)
            {
                c.IsCurrent = false;
                c.Status = "Archived";
                c.RetiredAt = DateTimeOffset.UtcNow;
                c.UpdatedDate = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(ct);
            return (true, null, draft);
        }

        public LockfileDiff Diff(StudyPlanLockfile from, StudyPlanLockfile to)
        {
            var result = new LockfileDiff();
            var fromMap = from.Lessons.Select((x, i) => (item: x, idx: i + 1)).ToDictionary(t => t.item.LessonStableId, t => (t.item.LessonVersionNumber, t.idx));
            var toMap = to.Lessons.Select((x, i) => (item: x, idx: i + 1)).ToDictionary(t => t.item.LessonStableId, t => (t.item.LessonVersionNumber, t.idx));

            // added / removed
            foreach (var sid in toMap.Keys.Except(fromMap.Keys))
            {
                result.Changes.Add(new LockfileChange { Type = "added", LessonStableId = sid, ToVersionNumber = toMap[sid].Item1, ToIndex = toMap[sid].Item2 });
                result.Summary.Added++;
            }
            foreach (var sid in fromMap.Keys.Except(toMap.Keys))
            {
                result.Changes.Add(new LockfileChange { Type = "removed", LessonStableId = sid, FromVersionNumber = fromMap[sid].Item1, FromIndex = fromMap[sid].Item2 });
                result.Summary.Removed++;
            }

            // upgraded / downgraded / reordered
            foreach (var sid in fromMap.Keys.Intersect(toMap.Keys))
            {
                var (fv, fi) = fromMap[sid];
                var (tv, ti) = toMap[sid];
                if (tv > fv)
                {
                    result.Changes.Add(new LockfileChange { Type = "upgraded", LessonStableId = sid, FromVersionNumber = fv, ToVersionNumber = tv, FromIndex = fi, ToIndex = ti });
                    result.Summary.Upgraded++;
                }
                else if (tv < fv)
                {
                    result.Changes.Add(new LockfileChange { Type = "downgraded", LessonStableId = sid, FromVersionNumber = fv, ToVersionNumber = tv, FromIndex = fi, ToIndex = ti });
                    result.Summary.Downgraded++;
                }
                if (fi != ti)
                {
                    result.Changes.Add(new LockfileChange { Type = "reordered", LessonStableId = sid, FromIndex = fi, ToIndex = ti });
                    result.Summary.Reordered++;
                }
            }

            return result;
        }

        public static StudyPlanLockfile ParseLockfile(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new StudyPlanLockfile();
            try { return JsonSerializer.Deserialize<StudyPlanLockfile>(json) ?? new StudyPlanLockfile(); }
            catch { return new StudyPlanLockfile(); }
        }
    }
}

