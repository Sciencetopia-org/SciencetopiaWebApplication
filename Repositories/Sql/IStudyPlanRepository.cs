// IStudyPlanRepository.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Models;

public interface IStudyPlanRepository
{
    Task InsertStudyPlanAsync(StudyPlanEntity studyPlan);
    Task UpdateStudyPlanAsync(StudyPlanEntity studyPlan);
    Task InsertLessonAsync(LessonEntity lesson);
    Task UpdateLessonAsync(LessonEntity lesson);
    Task InsertResourceAsync(Resource resource);
    Task UpdateResourceAsync(Resource resource);
    Task<List<StudyPlanEntity>> GetStudyPlansByUserIdAsync(string userId);
    Task<StudyPlanEntity?> GetStudyPlanByIdAsync(string studyPlanId);
    Task<List<LessonEntity>> GetLessonsByIdsAsync(List<string> lessonIds);
    Task<List<Resource>> GetResourcesByIdsAsync(List<string> resourceIds);
    Task DeleteStudyPlanByIdAsync(string studyPlanId);
    Task<int> GetNextStudyPlanVersionNumberAsync(Guid stableId);
    Task<StudyPlanEntity?> GetPlanByStableIdAsync(Guid stableId, int? versionNumber = null);
    Task<int> GetNextLessonVersionNumberAsync(Guid lessonStableId);
    Task<LessonEntity?> GetLessonByStableIdAsync(Guid stableId, int? versionNumber = null);
    Task<LessonEntity?> GetLessonByIdAsync(Guid id);
    Task ReplacePlanLessonSnapshotsAsync(Guid planStableId, int planVersionNumber, IEnumerable<Sciencetopia.Models.StudyPlanLessonSnapshot> snapshots);
    Task<List<Sciencetopia.Models.StudyPlanLessonSnapshot>> GetPlanLessonSnapshotsAsync(Guid planStableId, int planVersionNumber);
    Task<List<Guid>> GetPlanTagStableIdsAsync(Guid planStableId, int planVersionNumber);
    Task ReplacePlanTagAssignmentsAsync(Guid planStableId, int planVersionNumber, IEnumerable<Guid> tagStableIds);
    Task<List<Guid>> GetLessonTagStableIdsAsync(Guid lessonStableId, int lessonVersionNumber);
    Task ReplaceLessonTagAssignmentsAsync(Guid lessonStableId, int lessonVersionNumber, IEnumerable<Guid> tagStableIds);
}

public class StudyPlanRepository : IStudyPlanRepository
{
    private readonly ApplicationDbContext _dbContext;

    public StudyPlanRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task InsertStudyPlanAsync(StudyPlanEntity studyPlan)
    {
        if (studyPlan.StableId == Guid.Empty)
        {
            studyPlan.StableId = studyPlan.Id;
        }

        if (studyPlan.VersionNumber <= 0)
        {
            studyPlan.VersionNumber = 1;
        }

        if (string.IsNullOrWhiteSpace(studyPlan.Status))
        {
            studyPlan.Status = "Current";
        }

        if (string.Equals(studyPlan.Status, "Current", StringComparison.OrdinalIgnoreCase))
        {
            if (!studyPlan.IsCurrent)
            {
                studyPlan.IsCurrent = true;
            }
        }

        var utcNow = DateTimeOffset.UtcNow;
        studyPlan.CreatedAt ??= utcNow;
        if (studyPlan.IsCurrent)
        {
            studyPlan.PublishedAt ??= utcNow;
            studyPlan.ApprovedAt ??= utcNow;
        }

        if (string.IsNullOrWhiteSpace(studyPlan.CreatedBy))
        {
            studyPlan.CreatedBy = studyPlan.CreatorId.ToString();
        }

        if (string.IsNullOrWhiteSpace(studyPlan.ApprovedBy))
        {
            studyPlan.ApprovedBy = studyPlan.CreatedBy;
        }

        if (studyPlan.CreatedDate == default)
        {
            studyPlan.CreatedDate = DateTime.UtcNow;
        }

        studyPlan.UpdatedDate = DateTime.UtcNow;

        await _dbContext.StudyPlans.AddAsync(studyPlan);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateStudyPlanAsync(StudyPlanEntity studyPlan)
    {
        studyPlan.UpdatedDate = DateTime.UtcNow;
        studyPlan.CreatedAt ??= DateTimeOffset.UtcNow;
        _dbContext.StudyPlans.Update(studyPlan);
        await _dbContext.SaveChangesAsync();
    }

    public async Task InsertLessonAsync(LessonEntity lesson)
    {
        if (lesson.StableId == Guid.Empty)
        {
            lesson.StableId = lesson.Id;
        }

        if (lesson.VersionNumber <= 0)
        {
            lesson.VersionNumber = 1;
        }

        if (string.IsNullOrWhiteSpace(lesson.Status))
        {
            lesson.Status = "Current";
        }

        lesson.IsCurrent = true;

        var utcNow = DateTimeOffset.UtcNow;
        lesson.CreatedAt ??= utcNow;
        lesson.PublishedAt ??= utcNow;
        lesson.ApprovedAt ??= utcNow;

        if (string.IsNullOrWhiteSpace(lesson.ApprovedBy))
        {
            lesson.ApprovedBy = lesson.CreatedBy;
        }

        if (lesson.CreatedDate == default)
        {
            lesson.CreatedDate = DateTime.UtcNow;
        }

        lesson.UpdatedDate = DateTime.UtcNow;

        await _dbContext.Lessons.AddAsync(lesson);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateLessonAsync(LessonEntity lesson)
    {
        lesson.UpdatedDate = DateTime.UtcNow;
        lesson.CreatedAt ??= DateTimeOffset.UtcNow;
        _dbContext.Lessons.Update(lesson);
        await _dbContext.SaveChangesAsync();
    }

    public async Task InsertResourceAsync(Resource resource)
    {
        await _dbContext.Resources.AddAsync(resource);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateResourceAsync(Resource resource)
    {
        _dbContext.Resources.Update(resource);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<List<StudyPlanEntity>> GetStudyPlansByUserIdAsync(string userId)
    {
        return await _dbContext.StudyPlans
            .Where(sp => sp.CreatorId == Guid.Parse(userId))
            .Select(sp => new StudyPlanEntity
            {
                Id = sp.Id,
                StableId = sp.StableId,
                VersionNumber = sp.VersionNumber,
                Status = sp.Status,
                IsCurrent = sp.IsCurrent,
                PublishedAt = sp.PublishedAt,
                RetiredAt = sp.RetiredAt,
                CreatedAt = sp.CreatedAt,
                CreatedBy = sp.CreatedBy,
                ApprovedAt = sp.ApprovedAt,
                ApprovedBy = sp.ApprovedBy,
                RowVersion = sp.RowVersion ?? Array.Empty<byte>(),
                Title = sp.Title,
                Description = sp.Description,
                CreatorId = sp.CreatorId,
                CreatedDate = sp.CreatedDate,
                UpdatedDate = sp.UpdatedDate
            })
            .ToListAsync();
    }

    public async Task<StudyPlanEntity?> GetStudyPlanByIdAsync(string studyPlanId)
    {
        if (!Guid.TryParse(studyPlanId, out var studyPlanGuid))
            return null;

        return await _dbContext.StudyPlans
            .Where(sp => sp.Id == studyPlanGuid)
            .FirstOrDefaultAsync();
    }

    public async Task<List<LessonEntity>> GetLessonsByIdsAsync(List<string> lessonIds)
    {
        if (lessonIds == null || lessonIds.Count == 0)
            return new List<LessonEntity>();

        var guidIds = lessonIds
            .Select(id => Guid.TryParse(id, out var guid) ? guid : (Guid?)null)
            .Where(guid => guid.HasValue)
            .Select(guid => guid.Value)
            .ToList();

        return await _dbContext.Lessons
            .Where(l => guidIds.Contains(l.Id))
            .ToListAsync();
    }

    public async Task<List<Resource>> GetResourcesByIdsAsync(List<string> resourceIds)
    {
        if (resourceIds == null || resourceIds.Count == 0)
            return new List<Resource>();

        var guidIds = resourceIds
            .Select(id => Guid.TryParse(id, out var guid) ? guid : (Guid?)null)
            .Where(guid => guid.HasValue)
            .Select(guid => guid.Value)
            .ToList();

        return await _dbContext.Resources
            .Where(r => guidIds.Contains(r.Id))
            .ToListAsync();
    }

    public async Task DeleteStudyPlanByIdAsync(string studyPlanId)
    {
        var id = Guid.Parse(studyPlanId);

        var studyPlan = await _dbContext.StudyPlans.FindAsync(id);
        if (studyPlan != null)
        {
            var stableId = studyPlan.StableId == Guid.Empty ? studyPlan.Id : studyPlan.StableId;
            var versionNumber = studyPlan.VersionNumber;

            var snapshotPairs = await _dbContext.StudyPlanLessonSnapshots
                .Where(s => s.StudyPlanStableId == stableId && s.StudyPlanVersionNumber == versionNumber)
                .Select(s => new { s.LessonStableId, s.LessonVersionNumber })
                .ToListAsync();

            var snapshots = _dbContext.StudyPlanLessonSnapshots
                .Where(s => s.StudyPlanStableId == stableId && s.StudyPlanVersionNumber == versionNumber);
            _dbContext.StudyPlanLessonSnapshots.RemoveRange(snapshots);

            var planTags = _dbContext.StudyPlanTagAssignments.Where(t => t.StudyPlanStableId == stableId && t.StudyPlanVersionNumber == versionNumber);
            _dbContext.StudyPlanTagAssignments.RemoveRange(planTags);

            if (snapshotPairs.Count > 0)
            {
                var lessonStableSet = snapshotPairs.Select(p => p.LessonStableId).Distinct().ToHashSet();
                var lessonAssignments = await _dbContext.LessonTagAssignments
                    .Where(t => lessonStableSet.Contains(t.LessonStableId))
                    .ToListAsync();

                if (lessonAssignments.Count > 0)
                {
                    var pairSet = snapshotPairs
                        .Select(p => (p.LessonStableId, p.LessonVersionNumber))
                        .ToHashSet();
                    var targets = lessonAssignments
                        .Where(t => pairSet.Contains((t.LessonStableId, t.LessonVersionNumber)))
                        .ToList();
                    if (targets.Count > 0)
                    {
                        _dbContext.LessonTagAssignments.RemoveRange(targets);
                    }
                }
            }

            _dbContext.StudyPlans.Remove(studyPlan);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<int> GetNextStudyPlanVersionNumberAsync(Guid stableId)
    {
        var max = await _dbContext.StudyPlans
            .Where(sp => sp.StableId == stableId)
            .Select(sp => (int?)sp.VersionNumber)
            .MaxAsync();
        return (max ?? 0) + 1;
    }

    public async Task<StudyPlanEntity?> GetPlanByStableIdAsync(Guid stableId, int? versionNumber = null)
    {
        var query = _dbContext.StudyPlans.Where(sp => sp.StableId == stableId);
        if (versionNumber.HasValue)
        {
            query = query.Where(sp => sp.VersionNumber == versionNumber.Value);
        }
        else
        {
            query = query.Where(sp => sp.IsCurrent);
        }

        return await query
            .OrderByDescending(sp => sp.VersionNumber)
            .FirstOrDefaultAsync();
    }

    public async Task<int> GetNextLessonVersionNumberAsync(Guid lessonStableId)
    {
        var max = await _dbContext.Lessons
            .Where(l => l.StableId == lessonStableId)
            .Select(l => (int?)l.VersionNumber)
            .MaxAsync();
        return (max ?? 0) + 1;
    }

    public async Task<LessonEntity?> GetLessonByStableIdAsync(Guid stableId, int? versionNumber = null)
    {
        var query = _dbContext.Lessons.Where(l => l.StableId == stableId);
        if (versionNumber.HasValue)
        {
            query = query.Where(l => l.VersionNumber == versionNumber.Value);
        }
        else
        {
            query = query.Where(l => l.IsCurrent);
        }

        return await query
            .OrderByDescending(l => l.VersionNumber)
            .FirstOrDefaultAsync();
    }

    public async Task<LessonEntity?> GetLessonByIdAsync(Guid id)
    {
        return await _dbContext.Lessons.FirstOrDefaultAsync(l => l.Id == id);
    }

    public async Task ReplacePlanLessonSnapshotsAsync(Guid planStableId, int planVersionNumber, IEnumerable<StudyPlanLessonSnapshot> snapshots)
    {
        var existing = _dbContext.StudyPlanLessonSnapshots
            .Where(s => s.StudyPlanStableId == planStableId && s.StudyPlanVersionNumber == planVersionNumber);

        _dbContext.StudyPlanLessonSnapshots.RemoveRange(existing);
        await _dbContext.SaveChangesAsync();

        if (snapshots.Any())
        {
            await _dbContext.StudyPlanLessonSnapshots.AddRangeAsync(snapshots);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<List<StudyPlanLessonSnapshot>> GetPlanLessonSnapshotsAsync(Guid planStableId, int planVersionNumber)
    {
        return await _dbContext.StudyPlanLessonSnapshots
            .AsNoTracking()
            .Where(s => s.StudyPlanStableId == planStableId && s.StudyPlanVersionNumber == planVersionNumber)
            .OrderBy(s => s.StepOrder)
            .ToListAsync();
    }

    public async Task<List<Guid>> GetPlanTagStableIdsAsync(Guid planStableId, int planVersionNumber)
    {
        return await _dbContext.StudyPlanTagAssignments
            .AsNoTracking()
            .Where(t => t.StudyPlanStableId == planStableId && t.StudyPlanVersionNumber == planVersionNumber)
            .Select(t => t.TagStableId)
            .Distinct()
            .ToListAsync();
    }

    public async Task ReplacePlanTagAssignmentsAsync(Guid planStableId, int planVersionNumber, IEnumerable<Guid> tagStableIds)
    {
        var normalized = tagStableIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? new List<Guid>();

        var existing = _dbContext.StudyPlanTagAssignments
            .Where(t => t.StudyPlanStableId == planStableId && t.StudyPlanVersionNumber == planVersionNumber);
        _dbContext.StudyPlanTagAssignments.RemoveRange(existing);
        await _dbContext.SaveChangesAsync();

        if (normalized.Count > 0)
        {
            var assignments = normalized.Select(id => new StudyPlanTagAssignment
            {
                StudyPlanStableId = planStableId,
                StudyPlanVersionNumber = planVersionNumber,
                TagStableId = id,
                AssignedAt = DateTime.UtcNow
            });
            await _dbContext.StudyPlanTagAssignments.AddRangeAsync(assignments);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<List<Guid>> GetLessonTagStableIdsAsync(Guid lessonStableId, int lessonVersionNumber)
    {
        return await _dbContext.LessonTagAssignments
            .AsNoTracking()
            .Where(t => t.LessonStableId == lessonStableId && t.LessonVersionNumber == lessonVersionNumber)
            .Select(t => t.TagStableId)
            .Distinct()
            .ToListAsync();
    }

    public async Task ReplaceLessonTagAssignmentsAsync(Guid lessonStableId, int lessonVersionNumber, IEnumerable<Guid> tagStableIds)
    {
        var normalized = tagStableIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? new List<Guid>();

        var existing = _dbContext.LessonTagAssignments
            .Where(t => t.LessonStableId == lessonStableId && t.LessonVersionNumber == lessonVersionNumber);
        _dbContext.LessonTagAssignments.RemoveRange(existing);
        await _dbContext.SaveChangesAsync();

        if (normalized.Count > 0)
        {
            var assignments = normalized.Select(id => new LessonTagAssignment
            {
                LessonStableId = lessonStableId,
                LessonVersionNumber = lessonVersionNumber,
                TagStableId = id,
                AssignedAt = DateTime.UtcNow
            });
            await _dbContext.LessonTagAssignments.AddRangeAsync(assignments);
            await _dbContext.SaveChangesAsync();
        }
    }

}
