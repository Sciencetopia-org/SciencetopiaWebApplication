using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Sciencetopia.Data;
using Sciencetopia.DTOs;
using Sciencetopia.Models;

namespace Sciencetopia.Services.Cohorts;

public interface ICohortService
{
    Task<CohortViewDto> CreateAsync(Guid planId, string createdBy, CohortCreateDto dto);
    Task<CohortViewDto?> UpdateAsync(Guid cohortId, CohortUpdateDto dto);
    Task<List<CohortViewDto>> ListByPlanAsync(Guid planId);
    Task<Guid?> GetPlanIdAsync(Guid cohortId);
    Task DeleteAsync(Guid cohortId, CancellationToken ct = default);

    Task UnenrollAsync(Guid cohortId, string userId);

    Task AutoEnrollGroupAsync(Guid cohortId, Guid groupId, bool enabled = true);
    Task RemoveAutoEnrollGroupAsync(Guid cohortId, Guid groupId);
    Task<int> RunAutoEnrollAsync(Guid cohortId, Guid groupId, CancellationToken ct = default);

    // B3-1 JoinCohortAsync
    Task<(Guid planId, Guid cohortId)> JoinCohortAsync(Guid cohortId, string userId, bool shareMetrics = true, CancellationToken ct = default);
    // B3-2 SwitchCohortAsync
    Task<(Guid planId, Guid? fromCohortId, Guid toCohortId)> SwitchCohortAsync(Guid planId, Guid toCohortId, string userId, bool shareMetrics = true, string? migrationStrategy = null, CancellationToken ct = default);

    Task<Sciencetopia.DTOs.EnrollmentMeDto> GetEnrollmentForUserAsync(Guid planId, string userId, CancellationToken ct = default);

    
    Task<int> EnsureEnrollmentToCurrentVersionAsync(Guid planId, string userId, CancellationToken ct = default);

    // New unified entries
    Task EnsureEnrollmentAsync(string userId, Guid planVersionId, CancellationToken ct = default);
    Task EnsureCohortMembershipAsync(string userId, Guid cohortId, CancellationToken ct = default);
    Task<(bool versionMismatch, Guid planVersionId)> AutoEnrollToCohortAsync(string userId, Guid cohortId, CancellationToken ct = default);
    Task AlignEnrollmentVersionAsync(string userId, Guid targetVersionId, CancellationToken ct = default);
    Task<int> MigrateProgressAsync(string userId, Guid fromVersionId, Guid toVersionId, CancellationToken ct = default);
}

    public class CohortService : ICohortService
    {
        private readonly ApplicationDbContext _db;
        private readonly IDriver _driver;

    public CohortService(ApplicationDbContext db, IDriver driver)
    {
        _db = db;
        _driver = driver;
    }

    public async Task<Sciencetopia.DTOs.EnrollmentMeDto> GetEnrollmentForUserAsync(Guid planId, string userId, CancellationToken ct = default)
    {
        var dto = new Sciencetopia.DTOs.EnrollmentMeDto();

        // Fetch active cohort via Neo4j
        await using var session = _driver.AsyncSession();
        var record = await session.ExecuteReadAsync(async tx =>
        {
            var cypher = @"
MATCH (p:StudyPlan {id:$planId})
OPTIONAL MATCH (u:User {id:$userId})-[:IN_COHORT]->(c:Cohort)-[:FOR_PLAN]->(p)
WITH p, c
OPTIONAL MATCH (u2:User {id:$userId})-[:IN_COHORT]->(c2:Cohort)-[:FOR_PLAN]->(p)
RETURN coalesce(c.id,'') AS activeId, collect(c2.id) AS participated";
            var cursor = await tx.RunAsync(cypher, new { planId = planId.ToString(), userId });
            return await cursor.SingleAsync();
        });

        Guid? activeId = null;
        var activeStr = record["activeId"].As<string>();
        if (!string.IsNullOrEmpty(activeStr))
            activeId = Guid.Parse(activeStr);

        dto.ActiveCohortId = activeId;
        dto.JoinedAt = null;

        // Determine role (group roles only)
        string? role = null;
        if (activeId.HasValue)
        {
            // If group-scoped, derive from SQL group membership
            var groupId = await _db.Cohorts.AsNoTracking()
                .Where(x => x.Id == activeId.Value)
                .Select(x => x.StudyGroupId)
                .FirstOrDefaultAsync(ct);
            if (groupId.HasValue)
            {
                var isManager = await _db.StudyGroupUserRoles.AsNoTracking()
                    .AnyAsync(x => x.GroupId == groupId.Value && x.UserId == userId && x.Role == Models.Enums.GroupRole.Manager, ct);
                role = isManager ? "manager" : "member";
            }
        }
        dto.Role = role;

        // Archived = participated minus active
        var arr = record["participated"].As<List<object>>();
        foreach (var o in arr)
        {
            var s = o?.ToString();
            if (Guid.TryParse(s, out var gid) && (!activeId.HasValue || gid != activeId.Value))
                dto.ArchivedCohortIds.Add(gid);
        }

        return dto;
    }

    public async Task<CohortViewDto> CreateAsync(Guid planId, string createdBy, CohortCreateDto dto)
    {
        var entity = new StudyPlanCohort
        {
            Id = Guid.NewGuid(),
            StudyPlanId = planId,
            Title = dto.Title,
            Visibility = dto.Visibility ?? "private",
            StartAt = dto.StartAt,
            EndAt = dto.EndAt,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };

        _db.Cohorts.Add(entity);
        await _db.SaveChangesAsync();

        await UpsertCohortNodeAsync(entity);

        return ToDto(entity);
    }

    public async Task<CohortViewDto?> UpdateAsync(Guid cohortId, CohortUpdateDto dto)
    {
        var entity = await _db.Cohorts.FirstOrDefaultAsync(x => x.Id == cohortId);
        if (entity == null) return null;

        if (dto.Title != null) entity.Title = dto.Title;
        if (dto.Visibility != null) entity.Visibility = dto.Visibility;
        entity.StartAt = dto.StartAt;
        entity.EndAt = dto.EndAt;

        await _db.SaveChangesAsync();
        await UpsertCohortNodeAsync(entity);
        return ToDto(entity);
    }

    public async Task<List<CohortViewDto>> ListByPlanAsync(Guid planId)
    {
        var list = await _db.Cohorts.AsNoTracking()
            .Where(x => x.StudyPlanId == planId)
            .OrderBy(x => x.StartAt ?? x.CreatedAt)
            .ToListAsync();
        return list.Select(ToDto).ToList();
    }

    public async Task DeleteAsync(Guid cohortId, CancellationToken ct = default)
    {
        // Remove SQL entity if exists
        var entity = await _db.Cohorts.FirstOrDefaultAsync(x => x.Id == cohortId, ct);
        if (entity != null)
        {
            _db.Cohorts.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }

        // Remove Neo4j node and relationships
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"MATCH (c:Cohort {id:$id}) DETACH DELETE c";
            await tx.RunAsync(cypher, new { id = cohortId.ToString() });
        });
    }

    public async Task<Guid?> GetPlanIdAsync(Guid cohortId)
        => await _db.Cohorts.Where(x => x.Id == cohortId)
                            .Select(x => (Guid?)x.StudyPlanId)
                            .FirstOrDefaultAsync();

    // Removed EnrollAsync (deprecated)

    public async Task UnenrollAsync(Guid cohortId, string userId)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (u:User {id:$userId})
MATCH (c:Cohort {id:$cohortId})-[:OF_VERSION]->(pv:PlanVersion)
MATCH (u)-[r:ENROLLED_IN]->(pv)
DELETE r
";
            await tx.RunAsync(cypher, new { userId, cohortId = cohortId.ToString() });
        });
    }

    public async Task AutoEnrollGroupAsync(Guid cohortId, Guid groupId, bool enabled = true)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (g:StudyGroup {id:$groupId})
MATCH (c:Cohort {id:$cohortId})
MERGE (g)-[r:AUTO_ENROLL]->(c)
SET r.enabled=$enabled, r.createdAt = coalesce(r.createdAt, datetime())
";
            await tx.RunAsync(cypher, new { cohortId = cohortId.ToString(), groupId = groupId.ToString(), enabled });
        });
    }

    public async Task RemoveAutoEnrollGroupAsync(Guid cohortId, Guid groupId)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (g:StudyGroup {id:$groupId})-[r:AUTO_ENROLL]->(c:Cohort {id:$cohortId})
DELETE r
";
            await tx.RunAsync(cypher, new { cohortId = cohortId.ToString(), groupId = groupId.ToString() });
        });
    }

    public async Task<int> RunAutoEnrollAsync(Guid cohortId, Guid groupId, CancellationToken ct = default)
    {
        await using var session = _driver.AsyncSession();
        var count = await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (g:StudyGroup {id:$groupId})<-[:MEMBER_OF]-(u:User)
MATCH (c:Cohort {id:$cohortId})-[:OF_VERSION]->(pv:PlanVersion)
MERGE (u)-[:ENROLLED_IN]->(pv)
RETURN count(DISTINCT u) AS processed";
            var cursor = await tx.RunAsync(cypher, new { cohortId = cohortId.ToString(), groupId = groupId.ToString() });
            var rec = await cursor.SingleAsync();
            return rec["processed"].As<int>();
        });
        return count;
    }

    public async Task<(Guid planId, Guid cohortId)> JoinCohortAsync(Guid cohortId, string userId, bool shareMetrics = true, CancellationToken ct = default)
    {
        // Validate cohort and group-scope access consistently with Enroll
        var planId = await EnsureCohortScopeAccessAsync(cohortId, userId, ct);

        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (u:User {id:$userId})
MATCH (c:Cohort {id:$cohortId})
MERGE (u)-[:IN_COHORT]->(c)
WITH u, c
MATCH (c)-[:OF_VERSION]->(pv:PlanVersion)
MERGE (u)-[:ENROLLED_IN]->(pv)
RETURN c.id AS cohortId;";
            await tx.RunAsync(cypher, new { userId, cohortId = cohortId.ToString() });
        });

        return (planId, cohortId);
    }

    private async Task<Guid> EnsureCohortScopeAccessAsync(Guid cohortId, string userId, CancellationToken ct)
    {
        var info = await _db.Cohorts.AsNoTracking()
            .Where(x => x.Id == cohortId)
            .Select(x => new { x.StudyPlanId, x.StudyGroupId })
            .FirstOrDefaultAsync(ct);
        if (info == null) throw new KeyNotFoundException("cohort_not_found");

        if (info.StudyGroupId.HasValue)
        {
            var isMember = await _db.StudyGroupUserRoles.AsNoTracking()
                .AnyAsync(x => x.GroupId == info.StudyGroupId.Value && x.UserId == userId, ct);
            if (!isMember) throw new UnauthorizedAccessException("forbidden_not_group_member");
        }

        return info.StudyPlanId;
    }

    public async Task<(Guid planId, Guid? fromCohortId, Guid toCohortId)> SwitchCohortAsync(Guid planId, Guid toCohortId, string userId, bool shareMetrics = true, string? migrationStrategy = null, CancellationToken ct = default)
    {
        // Validate target cohort belongs to plan
        var target = await _db.Cohorts.AsNoTracking()
            .Where(x => x.Id == toCohortId)
            .Select(x => new { x.StudyPlanId, x.StudyGroupId })
            .FirstOrDefaultAsync(ct);
        if (target == null) throw new KeyNotFoundException("cohort_not_found");
        if (target.StudyPlanId != planId) throw new InvalidOperationException("cohort_plan_mismatch");

        if (target.StudyGroupId.HasValue)
        {
            var isMember = await _db.StudyGroupUserRoles.AsNoTracking()
                .AnyAsync(x => x.GroupId == target.StudyGroupId.Value && x.UserId == userId, ct);
            if (!isMember) throw new UnauthorizedAccessException("forbidden_not_group_member");
        }

        Guid? fromId = null;
        await using var session = _driver.AsyncSession();
        var result = await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (to:Cohort {id:$toCohortId})-[:OF_VERSION]->(toPv:PlanVersion)
MERGE (u:User {id:$userId})-[:IN_COHORT]->(to)
// Ensure enrollment to target cohort's PlanVersion (no deletion of prior)
MERGE (u)-[:ENROLLED_IN]->(toPv)
RETURN '' AS fromCohortId, to.id AS toCohortId;";
            var cursor = await tx.RunAsync(cypher, new { userId, toCohortId = toCohortId.ToString() });
            return await cursor.SingleAsync();
        });
        var fromStr = result["fromCohortId"].As<string>();
        if (!string.IsNullOrEmpty(fromStr))
            fromId = Guid.Parse(fromStr);

        return (planId, fromId, toCohortId);
    }

    private async Task UpsertCohortNodeAsync(StudyPlanCohort c)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (p:StudyPlan {id:$planId})
MERGE (c:Cohort {id:$id})
SET c.title=$title, c.visibility=$visibility, c.startAt=$startAt, c.endAt=$endAt
MERGE (c)-[:FOR_PLAN]->(p)
";
            await tx.RunAsync(cypher, new
            {
                id = c.Id,
                planId = c.StudyPlanId,
                title = c.Title,
                visibility = c.Visibility,
                startAt = c.StartAt,
                endAt = c.EndAt
            });
        });
    }

    public async Task EnsureEnrollmentAsync(string userId, Guid planVersionId, CancellationToken ct = default)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"MATCH (pv:PlanVersion {id:$pvId}) MERGE (:User {id:$userId})-[:ENROLLED_IN]->(pv)";
            await tx.RunAsync(cypher, new { userId, pvId = planVersionId.ToString() });
        });
    }

    public async Task EnsureCohortMembershipAsync(string userId, Guid cohortId, CancellationToken ct = default)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"MERGE (:User {id:$userId})-[:IN_COHORT]->(:Cohort {id:$cohortId})";
            await tx.RunAsync(cypher, new { userId, cohortId = cohortId.ToString() });
        });
    }

    public async Task<(bool versionMismatch, Guid planVersionId)> AutoEnrollToCohortAsync(string userId, Guid cohortId, CancellationToken ct = default)
    {
        await EnsureCohortMembershipAsync(userId, cohortId, ct);
        await using var session = _driver.AsyncSession();
        var result = await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (c:Cohort {id:$cohortId})-[:OF_VERSION]->(pv:PlanVersion)
MERGE (:User {id:$userId})-[:ENROLLED_IN]->(pv)
OPTIONAL MATCH (:User {id:$userId})-[:ENROLLED_IN]->(other:PlanVersion {studyPlanId: pv.studyPlanId})
WITH pv, collect(other.versionNumber) AS nums
RETURN any(x IN nums WHERE x <> pv.versionNumber) AS mismatch, pv.id AS pvId";
            var cur = await tx.RunAsync(cypher, new { userId, cohortId = cohortId.ToString() });
            return await cur.SingleAsync();
        });
        var mismatch = result["mismatch"].As<bool>();
        var pvIdStr = result["pvId"].As<string>();
        Guid.TryParse(pvIdStr, out var pvId);
        return (mismatch, pvId);
    }

    public async Task AlignEnrollmentVersionAsync(string userId, Guid targetVersionId, CancellationToken ct = default)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (:User {id:$userId})-[r:ENROLLED_IN]->(old:PlanVersion)
MATCH (pv:PlanVersion {id:$pvId})
WHERE old.studyPlanId = pv.studyPlanId AND old.versionNumber <> pv.versionNumber
DELETE r
MERGE (:User {id:$userId})-[:ENROLLED_IN]->(pv)";
            await tx.RunAsync(cypher, new { userId, pvId = targetVersionId.ToString() });
        });
    }

    public async Task<int> MigrateProgressAsync(string userId, Guid fromVersionId, Guid toVersionId, CancellationToken ct = default)
    {
        await using var session = _driver.AsyncSession();
        return await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (from:PlanVersion {id:$fromId})-[:HAS_RESOURCE]->(r:Resource)
WITH collect(r) AS fromSet
MATCH (to:PlanVersion {id:$toId})-[:HAS_RESOURCE]->(r2:Resource)
WITH fromSet, collect(r2) AS toSet
WITH [x IN fromSet WHERE x IN toSet] AS common
MATCH (u:User {id:$userId})
UNWIND common AS res
MERGE (u)-[c:COMPLETED]->(res)
RETURN count(res) AS migrated";
            var cur = await tx.RunAsync(cypher, new { userId, fromId = fromVersionId.ToString(), toId = toVersionId.ToString() });
            var rec = await cur.SingleAsync();
            return rec["migrated"].As<int>();
        });
    }

    

    public async Task<int> EnsureEnrollmentToCurrentVersionAsync(Guid planId, string userId, CancellationToken ct = default)
    {
        // Resolve current or latest version number
        long? currentId = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.Id == planId)
            .Select(p => p.CurrentVersionId)
            .FirstOrDefaultAsync(ct);

        int versionNumber;
        if (currentId.HasValue)
        {
            versionNumber = await _db.StudyPlanVersions
                .Where(v => v.Id == currentId.Value)
                .Select(v => v.VersionNumber)
                .FirstOrDefaultAsync(ct);
        }
        else
        {
            versionNumber = await _db.StudyPlanVersions
                .Where(v => v.StudyPlanId == planId)
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => v.VersionNumber)
                .FirstOrDefaultAsync(ct);
        }

        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MERGE (pv:PlanVersion {studyPlanId:$planId, versionNumber:$versionNumber})
MERGE (u:User {id:$userId})
MERGE (u)-[:ENROLLED_IN]->(pv)";
            await tx.RunAsync(cypher, new { planId = planId.ToString(), userId, versionNumber });
        });

        return versionNumber;
    }

    private static CohortViewDto ToDto(StudyPlanCohort c)
        => new CohortViewDto(c.Id, c.StudyPlanId, c.Title, c.Visibility, c.StartAt, c.EndAt, c.CreatedBy, c.CreatedAt);
}
