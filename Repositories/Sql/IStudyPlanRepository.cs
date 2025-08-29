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

    // Drafts & Versions APIs
    Task<int> CreateStudyPlanDraftAsync(Guid studyPlanId, string? title, string? description, string snapshotJson, string? changeNotes, string? userId);
    Task<int> CreateStudyPlanVersionAsync(Guid studyPlanId, string? title, string? description, string snapshotJson, string? changeNotes, string? userId);
    Task<StudyPlanDraft?> GetStudyPlanDraftAsync(Guid studyPlanId, int draftNumber);
    Task<List<StudyPlanDraft>> GetStudyPlanDraftsAsync(Guid studyPlanId);
    Task<List<StudyPlanVersion>> GetStudyPlanVersionsAsync(Guid studyPlanId);
    Task MarkStudyPlanDraftStatusAsync(Guid studyPlanId, int draftNumber, DraftStatus status, string? updatedBy);
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
        await _dbContext.StudyPlans.AddAsync(studyPlan);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateStudyPlanAsync(StudyPlanEntity studyPlan)
    {
        _dbContext.StudyPlans.Update(studyPlan);
        await _dbContext.SaveChangesAsync();
    }

    public async Task InsertLessonAsync(LessonEntity lesson)
    {
        await _dbContext.Lessons.AddAsync(lesson);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateLessonAsync(LessonEntity lesson)
    {
        _dbContext. Lessons.Update(lesson);
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
                Title = sp.Title,
                Description = sp.Description,
                CreatorId = sp.CreatorId,
                CreatedDate = sp.CreatedDate,
                UpdatedDate = sp.UpdatedDate,
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
            _dbContext.StudyPlans.Remove(studyPlan);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<int> CreateStudyPlanDraftAsync(Guid studyPlanId, string? title, string? description, string snapshotJson, string? changeNotes, string? userId)
    {
        var last = await _dbContext.StudyPlanDrafts
            .Where(d => d.StudyPlanId == studyPlanId)
            .OrderByDescending(d => d.DraftNumber)
            .Select(d => d.DraftNumber)
            .FirstOrDefaultAsync();
        int nextNumber = last == 0 ? 1 : last + 1;

        var draft = new StudyPlanDraft
        {
            StudyPlanId = studyPlanId,
            DraftNumber = nextNumber,
            Title = title,
            Description = description,
            SnapshotJson = snapshotJson,
            ChangeNotes = changeNotes,
            DraftStatus = null,
            CreatedBy = userId,
            UpdatedBy = userId,
            CreatedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };
        _dbContext.StudyPlanDrafts.Add(draft);
        await _dbContext.SaveChangesAsync();
        return nextNumber;
    }

    public async Task<int> CreateStudyPlanVersionAsync(Guid studyPlanId, string? title, string? description, string snapshotJson, string? changeNotes, string? userId)
    {
        var last = await _dbContext.StudyPlanVersions
            .Where(v => v.StudyPlanId == studyPlanId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => v.VersionNumber)
            .FirstOrDefaultAsync();
        int nextNumber = last == 0 ? 1 : last + 1;

        var version = new StudyPlanVersion
        {
            StudyPlanId = studyPlanId,
            VersionNumber = nextNumber,
            Title = title,
            Description = description,
            SnapshotJson = snapshotJson,
            ChangeNotes = changeNotes,
            CreatedBy = userId,
            CreatedDate = DateTime.UtcNow
        };
        _dbContext.StudyPlanVersions.Add(version);
        await _dbContext.SaveChangesAsync();
        return nextNumber;
    }

    public async Task<StudyPlanDraft?> GetStudyPlanDraftAsync(Guid studyPlanId, int draftNumber)
    {
        return await _dbContext.StudyPlanDrafts
            .Where(d => d.StudyPlanId == studyPlanId && d.DraftNumber == draftNumber)
            .FirstOrDefaultAsync();
    }

    public async Task<List<StudyPlanDraft>> GetStudyPlanDraftsAsync(Guid studyPlanId)
    {
        return await _dbContext.StudyPlanDrafts
            .Where(d => d.StudyPlanId == studyPlanId)
            .OrderByDescending(d => d.DraftNumber)
            .ToListAsync();
    }

    public async Task<List<StudyPlanVersion>> GetStudyPlanVersionsAsync(Guid studyPlanId)
    {
        return await _dbContext.StudyPlanVersions
            .Where(v => v.StudyPlanId == studyPlanId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync();
    }

    public async Task MarkStudyPlanDraftStatusAsync(Guid studyPlanId, int draftNumber, DraftStatus status, string? updatedBy)
    {
        var draft = await _dbContext.StudyPlanDrafts
            .Where(d => d.StudyPlanId == studyPlanId && d.DraftNumber == draftNumber)
            .FirstOrDefaultAsync();
        if (draft == null) return;
        draft.DraftStatus = status;
        draft.UpdatedBy = updatedBy;
        draft.UpdatedDate = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }
}

