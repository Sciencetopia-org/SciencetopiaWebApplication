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
    Task<List<LessonEntity>> GetLessonsByStableIdsAsync(IEnumerable<Guid> stableIds);
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
            studyPlan.Status = "Draft";
        }

        if (string.Equals(studyPlan.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            studyPlan.Status = "Active";
        }

        if (string.Equals(studyPlan.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            studyPlan.IsCurrent = true;
        }
        else if (studyPlan.IsCurrent)
        {
            studyPlan.Status = "Active";
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
            lesson.Status = "Draft";
        }

        if (string.Equals(lesson.Status, "Current", StringComparison.OrdinalIgnoreCase))
        {
            lesson.Status = "Active";
        }

        lesson.IsCurrent = string.Equals(lesson.Status, "Active", StringComparison.OrdinalIgnoreCase);

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
        var userGuid = Guid.TryParse(userId, out var parsedUserId) ? parsedUserId : Guid.Empty;

        var enrolledStableIds = await _dbContext.UserGroups.AsNoTracking()
            .Where(ug => ug.UserId == userId && (string.IsNullOrEmpty(ug.Status) || ug.Status == "Active" || ug.Status == "active"))
            .Join(_dbContext.GroupPlanEnrollments.AsNoTracking().Where(e => e.Status == "Active"),
                ug => ug.GroupId,
                e => e.GroupId,
                (ug, e) => e.StudyPlanStableId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToListAsync();

        return await _dbContext.StudyPlans
            .Where(sp =>
                (userGuid != Guid.Empty && sp.CreatorId == userGuid)
                || sp.CreatedBy == userId
                || enrolledStableIds.Contains(sp.StableId)
                || (sp.StableId == Guid.Empty && enrolledStableIds.Contains(sp.Id)))
            .OrderByDescending(sp => sp.IsCurrent)
            .ThenByDescending(sp => sp.UpdatedDate)
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
            .AsNoTracking()
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
            .AsNoTracking()
            .Where(r => guidIds.Contains(r.Id))
            .ToListAsync();
    }

    public async Task<List<LessonEntity>> GetLessonsByStableIdsAsync(IEnumerable<Guid> stableIds)
    {
        var set = stableIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        if (set.Count == 0) return new List<LessonEntity>();

        return await _dbContext.Lessons
            .AsNoTracking()
            .Where(l => set.Contains(l.StableId))
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
        var query = _dbContext.StudyPlans.AsNoTracking().Where(sp => sp.StableId == stableId);
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
        var query = _dbContext.Lessons.AsNoTracking().Where(l => l.StableId == stableId);
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
        return await _dbContext.Lessons.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
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
        await Task.CompletedTask;
        return new List<Guid>();
    }

    public async Task ReplacePlanTagAssignmentsAsync(Guid planStableId, int planVersionNumber, IEnumerable<Guid> tagStableIds)
    {
        await Task.CompletedTask;
    }

}
