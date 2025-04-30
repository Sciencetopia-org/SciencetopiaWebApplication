// IStudyPlanRepository.cs

using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;

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
                Title = sp.Title,
                Description = sp.Description,
                CreatorId = sp.CreatorId,
                CreatedDate = sp.CreatedDate,
                UpdatedDate = sp.UpdatedDate,
                // Privacy = sp.Privacy // 如果有的话
            })
    .ToListAsync();
    }

    public async Task<StudyPlanEntity?> GetStudyPlanByIdAsync(string studyPlanId)
    {
        if (!Guid.TryParse(studyPlanId, out var studyPlanGuid))
            return null; // 如果studyPlanId格式不对，直接返回null

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

}
