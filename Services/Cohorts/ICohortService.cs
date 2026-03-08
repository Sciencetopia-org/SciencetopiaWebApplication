using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Sciencetopia.Data;
using Sciencetopia.DTOs;
using Sciencetopia.Models;

namespace Sciencetopia.Services.Cohorts;

public interface ICohortService
{
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

    private async Task<Guid> ResolvePlanStableIdAsync(Guid identifier, CancellationToken ct = default)
    {
        var stable = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.Id == identifier)
            .Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
            .FirstOrDefaultAsync(ct);
        if (stable != Guid.Empty)
        {
            return stable;
        }

        var exists = await _db.StudyPlans.AsNoTracking()
            .AnyAsync(p => p.StableId == identifier, ct);
        return exists ? identifier : Guid.Empty;
    }

    private async Task<(Guid stableId, int versionNumber)> ResolvePlanVersionKeyAsync(Guid versionId, CancellationToken ct = default)
    {
        var key = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.Id == versionId)
            .Select(p => new
            {
                StableId = p.StableId == Guid.Empty ? p.Id : p.StableId,
                p.VersionNumber
            })
            .FirstOrDefaultAsync(ct);
        if (key == null) return (Guid.Empty, 0);
        return (key.StableId, key.VersionNumber);
    }

    private async Task<Guid> ResolvePlanVersionIdAsync(Guid stableId, int versionNumber, CancellationToken ct = default)
    {
        return await _db.StudyPlans.AsNoTracking()
            .Where(p => (p.StableId == stableId || p.Id == stableId) && p.VersionNumber == versionNumber)
            .Select(p => p.Id)
            .FirstOrDefaultAsync(ct);
    }

    private static string ToGroupRoleLabel(Sciencetopia.Models.Enums.GroupRole role)
    {
        if (role == Sciencetopia.Models.Enums.GroupRole.Owner) return "Owner";
        if (role >= Sciencetopia.Models.Enums.GroupRole.Admin) return "Admin";
        return "Member";
    }

    private async Task<CohortOffering?> GetCurrentOfferingAsync(Guid cohortId, CancellationToken ct = default)
    {
        var cohort = await _db.Cohorts.AsNoTracking()
            .Where(c => c.Id == cohortId)
            .Select(c => new { c.Id, c.CurrentOfferingId })
            .FirstOrDefaultAsync(ct);
        if (cohort == null)
        {
            return null;
        }

        CohortOffering? offering = null;
        if (cohort.CurrentOfferingId.HasValue)
        {
            offering = await _db.CohortOfferings.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == cohort.CurrentOfferingId.Value, ct);
        }

        if (offering == null)
        {
            offering = await _db.CohortOfferings.AsNoTracking()
                .Where(o => o.CohortGroupId == cohort.Id && o.Status == "Active")
                .OrderByDescending(o => o.StartAt)
                .FirstOrDefaultAsync(ct);
        }

        return offering;
    }

    private async Task<(Guid planStableId, Guid planVersionId, Guid? studyGroupId)> GetCohortPlanInfoAsync(Guid cohortId, CancellationToken ct = default)
    {
        var cohort = await _db.Cohorts.AsNoTracking()
            .Where(c => c.Id == cohortId)
            .Select(c => new { c.StudyGroupId })
            .FirstOrDefaultAsync(ct);
        if (cohort == null)
        {
            return (Guid.Empty, Guid.Empty, null);
        }

        var offering = await GetCurrentOfferingAsync(cohortId, ct);
        if (offering == null)
        {
            return (Guid.Empty, Guid.Empty, cohort.StudyGroupId);
        }

        var stableId = await _db.StudyGroupStudyPlans.AsNoTracking()
            .Where(x => x.Id == offering.StudyGroupStudyPlanId)
            .Select(x => x.StudyPlanStableId)
            .FirstOrDefaultAsync(ct);

        return (stableId, offering.StudyPlanVersionId, cohort.StudyGroupId);
    }

    public async Task<Sciencetopia.DTOs.EnrollmentMeDto> GetEnrollmentForUserAsync(Guid planId, string userId, CancellationToken ct = default)
    {
        var dto = new Sciencetopia.DTOs.EnrollmentMeDto();
        var stableId = await ResolvePlanStableIdAsync(planId, ct);
        if (stableId == Guid.Empty)
        {
            return dto;
        }

        var rows = await _db.StudyPlanEnrollments.AsNoTracking()
            .Where(x => x.UserId == userId && x.ScopeType == "Cohort")
            .Join(_db.StudyPlans.AsNoTracking(),
                e => e.PlanVersionId,
                p => p.Id,
                (e, p) => new
                {
                    e.ScopeId,
                    e.Status,
                    e.EnrolledAt,
                    e.UpdatedAt,
                    StableId = p.StableId == Guid.Empty ? p.Id : p.StableId
                })
            .Where(x => x.StableId == stableId)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return dto;
        }

        var active = rows
            .Where(x => string.Equals(x.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.UpdatedAt ?? x.EnrolledAt)
            .FirstOrDefault()
            ?? rows.OrderByDescending(x => x.UpdatedAt ?? x.EnrolledAt).FirstOrDefault();

        if (active != null)
        {
            dto.ActiveCohortId = active.ScopeId;
            dto.JoinedAt = (active.UpdatedAt ?? active.EnrolledAt).ToUnixTimeMilliseconds();
        }

        dto.ArchivedCohortIds = rows
            .Select(x => x.ScopeId)
            .Where(id => !dto.ActiveCohortId.HasValue || id != dto.ActiveCohortId.Value)
            .Distinct()
            .ToList();

        if (dto.ActiveCohortId.HasValue)
        {
            var groupId = await _db.Cohorts.AsNoTracking()
                .Where(x => x.Id == dto.ActiveCohortId.Value)
                .Select(x => x.StudyGroupId)
                .FirstOrDefaultAsync(ct);
            if (groupId.HasValue && groupId.Value != Guid.Empty)
            {
                var membershipRole = await _db.UserGroups.AsNoTracking()
                    .Where(x => x.GroupId == groupId.Value && x.UserId == userId && x.Status == "Active")
                    .Select(x => (Sciencetopia.Models.Enums.GroupRole?)x.Role)
                    .FirstOrDefaultAsync(ct);
                if (membershipRole.HasValue)
                {
                    dto.Role = ToGroupRoleLabel(membershipRole.Value);
                }
            }
        }

        return dto;
    }

    // Removed CreateAsync: cohort creation must happen via group-scoped controller

    public async Task<CohortViewDto?> UpdateAsync(Guid cohortId, CohortUpdateDto dto)
    {
        var entity = await _db.Cohorts.FirstOrDefaultAsync(x => x.Id == cohortId);
        if (entity == null) return null;

        if (dto.Title != null) entity.Title = dto.Title;
        if (dto.Visibility != null) entity.Visibility = dto.Visibility;
        CohortOffering? offering = null;
        if (entity.CurrentOfferingId.HasValue)
        {
            offering = await _db.CohortOfferings.FirstOrDefaultAsync(o => o.Id == entity.CurrentOfferingId.Value);
        }
        if (offering == null)
        {
            offering = await _db.CohortOfferings.FirstOrDefaultAsync(o => o.CohortGroupId == entity.Id && o.Status == "Active");
        }
        if (offering != null)
        {
            if (dto.StartAt.HasValue)
            {
                offering.StartAt = dto.StartAt.Value;
            }
            offering.EndAt = dto.EndAt;
        }

        await _db.SaveChangesAsync();
        await UpsertCohortNodeAsync(entity);
        Guid stableId = Guid.Empty;
        if (offering != null)
        {
            stableId = await _db.StudyGroupStudyPlans.AsNoTracking()
                .Where(x => x.Id == offering.StudyGroupStudyPlanId)
                .Select(x => x.StudyPlanStableId)
                .FirstOrDefaultAsync();
        }
        return ToDto(entity, stableId, offering);
    }

    public async Task<List<CohortViewDto>> ListByPlanAsync(Guid planId)
    {
        var stableId = await ResolvePlanStableIdAsync(planId);
        if (stableId == Guid.Empty) return new List<CohortViewDto>();

        var list = await _db.Cohorts.AsNoTracking()
            .Join(_db.CohortOfferings.AsNoTracking(),
                c => c.CurrentOfferingId,
                o => o.Id,
                (c, o) => new { Cohort = c, Offering = o })
            .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                co => co.Offering.StudyGroupStudyPlanId,
                sgsp => sgsp.Id,
                (co, sgsp) => new
                {
                    co.Cohort,
                    co.Offering,
                    sgsp.StudyPlanStableId
                })
            .Where(x => x.StudyPlanStableId == stableId)
            .OrderBy(x => x.Offering.StartAt)
            .ToListAsync();

        return list.Select(x => ToDto(x.Cohort, x.StudyPlanStableId, x.Offering)).ToList();
    }

    public async Task DeleteAsync(Guid cohortId, CancellationToken ct = default)
    {
        // Remove SQL entity if exists
        var entity = await _db.Cohorts.FirstOrDefaultAsync(x => x.Id == cohortId, ct);
        if (entity != null)
        {
            _db.Cohorts.Remove(entity);
            var group = await _db.Groups.FindAsync(new object?[] { entity.Id }, ct);
            if (group != null)
            {
                _db.Groups.Remove(group);
            }
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
    {
        var planId = await _db.Cohorts.AsNoTracking()
            .Where(x => x.Id == cohortId && x.CurrentOfferingId != null)
            .Join(_db.CohortOfferings.AsNoTracking(),
                c => c.CurrentOfferingId,
                o => o.Id,
                (c, o) => o)
            .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                o => o.StudyGroupStudyPlanId,
                sgsp => sgsp.Id,
                (o, sgsp) => (Guid?)sgsp.StudyPlanStableId)
            .FirstOrDefaultAsync();

        if (planId.HasValue && planId.Value != Guid.Empty)
        {
            return planId;
        }

        return await _db.CohortOfferings.AsNoTracking()
            .Where(o => o.CohortGroupId == cohortId && o.Status == "Active")
            .OrderByDescending(o => o.StartAt)
            .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                o => o.StudyGroupStudyPlanId,
                sgsp => sgsp.Id,
                (o, sgsp) => (Guid?)sgsp.StudyPlanStableId)
            .FirstOrDefaultAsync();
    }

    // Removed EnrollAsync (deprecated)

    public async Task UnenrollAsync(Guid cohortId, string userId)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (u:User {id:$userId})
OPTIONAL MATCH (u)-[ic:IN_COHORT]->(:Cohort {id:$cohortId})
DELETE ic
WITH u
OPTIONAL MATCH (:Cohort {id:$cohortId})-[:OF_VERSION]->(pv:PlanVersion)
OPTIONAL MATCH (u)-[r:ENROLLED_IN]->(pv)
DELETE r
";
            await tx.RunAsync(cypher, new { userId, cohortId = cohortId.ToString() });
        });

        var membership = await _db.UserGroups
            .FirstOrDefaultAsync(x => x.GroupId == cohortId && x.UserId == userId);
        if (membership != null)
        {
            _db.UserGroups.Remove(membership);
            await _db.SaveChangesAsync();
        }

        var enrollment = await _db.StudyPlanEnrollments
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ScopeType == "Cohort" && x.ScopeId == cohortId);
        if (enrollment != null)
        {
            enrollment.Status = "Inactive";
            enrollment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();
        }
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
        var planInfo = await GetCohortPlanInfoAsync(cohortId, ct);
        var planVersionId = planInfo.planVersionId;
        if (planVersionId == Guid.Empty)
        {
            return 0;
        }

        var userIds = await _db.UserGroups.AsNoTracking()
            .Where(x => x.GroupId == groupId && x.Status == "Active")
            .Select(x => x.UserId)
            .ToListAsync(ct);

        if (userIds.Count == 0)
        {
            return 0;
        }

        var (stableId, versionNumber) = await ResolvePlanVersionKeyAsync(planVersionId, ct);
        var count = 0;
        if (stableId != Guid.Empty && versionNumber > 0)
        {
            await using var session = _driver.AsyncSession();
            count = await session.ExecuteWriteAsync(async tx =>
            {
                var cypher = @"
MERGE (pv:PlanVersion {studyPlanId:$planId, versionNumber:$versionNumber})
UNWIND $userIds AS uid
MERGE (u:User {id:uid})
MERGE (u)-[:ENROLLED_IN]->(pv)
RETURN count(DISTINCT uid) AS processed";
                var cursor = await tx.RunAsync(cypher, new
                {
                    planId = stableId.ToString(),
                    versionNumber,
                    userIds
                });
                var rec = await cursor.SingleAsync();
                return rec["processed"].As<int>();
            });
        }

        var existing = await _db.StudyPlanEnrollments
            .Where(x => x.ScopeType == "Cohort" && x.ScopeId == cohortId && userIds.Contains(x.UserId))
            .ToListAsync(ct);
        var existingMap = existing.ToDictionary(x => x.UserId, StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        foreach (var uid in userIds)
        {
            if (existingMap.TryGetValue(uid, out var enrollment))
            {
                enrollment.PlanVersionId = planVersionId;
                if (!string.Equals(enrollment.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    enrollment.Status = "Active";
                }
                enrollment.UpdatedAt = now;
            }
            else
            {
                _db.StudyPlanEnrollments.Add(new StudyPlanEnrollment
                {
                    UserId = uid,
                    ScopeType = "Cohort",
                    ScopeId = cohortId,
                    PlanVersionId = planVersionId,
                    Status = "Active",
                    EnrolledAt = now
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        return count;
    }

    public async Task<(Guid planId, Guid cohortId)> JoinCohortAsync(Guid cohortId, string userId, bool shareMetrics = true, CancellationToken ct = default)
    {
        // Validate cohort and group-scope access consistently with Enroll
        var (planId, planVersionId) = await EnsureCohortScopeAccessAsync(cohortId, userId, ct);
        var existingActive = await GetActiveCohortForPlanAsync(userId, planId, ct);
        if (existingActive.HasValue && existingActive.Value != cohortId)
        {
            throw new InvalidOperationException("alreadyInPlan");
        }

        await EnsureCohortMembershipAsync(userId, cohortId, ct);
        await DeactivateOtherPlanEnrollmentsAsync(userId, planId, cohortId, ct);

        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (c:Cohort {id:$cohortId})
MATCH (c)-[:OF_VERSION]->(pv:PlanVersion)
MERGE (u:User {id:$userId})
OPTIONAL MATCH (u)-[oldIn:IN_COHORT]->(:Cohort)-[:FOR_PLAN]->(:StudyPlan {id:$planId})
DELETE oldIn
WITH u, c, pv
OPTIONAL MATCH (u)-[oldEn:ENROLLED_IN]->(:PlanVersion {studyPlanId:$planId})
DELETE oldEn
MERGE (u)-[:IN_COHORT]->(c)
MERGE (u)-[:ENROLLED_IN]->(pv)
RETURN c.id AS cohortId;";
            await tx.RunAsync(cypher, new
            {
                userId,
                cohortId = cohortId.ToString(),
                planId = planId.ToString()
            });
        });

        await UpsertStudyPlanEnrollmentAsync(userId, cohortId, planVersionId, ct);

        return (planId, cohortId);
    }

    private async Task<(Guid planStableId, Guid planVersionId)> EnsureCohortScopeAccessAsync(Guid cohortId, string userId, CancellationToken ct)
    {
        var info = await GetCohortPlanInfoAsync(cohortId, ct);
        if (info.planVersionId == Guid.Empty) throw new KeyNotFoundException("cohort_not_found");

        // Strong coupling: cohort must be group-scoped and user must be a member
        if (!info.studyGroupId.HasValue || info.studyGroupId.Value == Guid.Empty)
            throw new InvalidOperationException("cohort_not_group_scoped");

        var isMember = await _db.UserGroups.AsNoTracking()
            .AnyAsync(x => x.GroupId == info.studyGroupId.Value && x.UserId == userId && x.Status == "Active", ct);
        if (!isMember) throw new UnauthorizedAccessException("forbidden_not_group_member");

        var stableId = info.planStableId != Guid.Empty
            ? info.planStableId
            : await ResolvePlanStableIdAsync(info.planVersionId, ct);
        if (stableId == Guid.Empty) throw new InvalidOperationException("cohort_plan_missing");
        return (stableId, info.planVersionId);
    }

    public async Task<(Guid planId, Guid? fromCohortId, Guid toCohortId)> SwitchCohortAsync(Guid planId, Guid toCohortId, string userId, bool shareMetrics = true, string? migrationStrategy = null, CancellationToken ct = default)
    {
        // Validate target cohort belongs to plan
        var target = await GetCohortPlanInfoAsync(toCohortId, ct);
        if (target.planVersionId == Guid.Empty) throw new KeyNotFoundException("cohort_not_found");
        var stableId = await ResolvePlanStableIdAsync(planId, ct);
        var targetStableId = target.planStableId != Guid.Empty
            ? target.planStableId
            : await ResolvePlanStableIdAsync(target.planVersionId, ct);
        if (stableId == Guid.Empty || targetStableId == Guid.Empty || targetStableId != stableId)
            throw new InvalidOperationException("cohort_plan_mismatch");

        if (!target.studyGroupId.HasValue || target.studyGroupId.Value == Guid.Empty)
            throw new InvalidOperationException("cohort_not_group_scoped");
        var isMember = await _db.UserGroups.AsNoTracking()
            .AnyAsync(x => x.GroupId == target.studyGroupId.Value && x.UserId == userId && x.Status == "Active", ct);
        if (!isMember) throw new UnauthorizedAccessException("forbidden_not_group_member");

        var fromId = await GetActiveCohortForPlanAsync(userId, stableId, ct);
        await EnsureCohortMembershipAsync(userId, toCohortId, ct);
        await DeactivateOtherPlanEnrollmentsAsync(userId, stableId, toCohortId, ct);

        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (to:Cohort {id:$toCohortId})-[:OF_VERSION]->(toPv:PlanVersion)
MERGE (u:User {id:$userId})
OPTIONAL MATCH (u)-[oldIn:IN_COHORT]->(:Cohort)-[:FOR_PLAN]->(:StudyPlan {id:$planId})
DELETE oldIn
WITH u, to, toPv
OPTIONAL MATCH (u)-[oldEn:ENROLLED_IN]->(:PlanVersion {studyPlanId:$planId})
DELETE oldEn
MERGE (u)-[:IN_COHORT]->(to)
MERGE (u)-[:ENROLLED_IN]->(toPv)
RETURN to.id AS toCohortId;";
            await tx.RunAsync(cypher, new
            {
                userId,
                toCohortId = toCohortId.ToString(),
                planId = stableId.ToString()
            });
        });

        await UpsertStudyPlanEnrollmentAsync(userId, toCohortId, target.planVersionId, ct);

        return (stableId, fromId, toCohortId);
    }

    private async Task<Guid?> GetActiveCohortForPlanAsync(string userId, Guid planStableId, CancellationToken ct)
    {
        return await _db.StudyPlanEnrollments.AsNoTracking()
            .Where(x => x.UserId == userId && x.ScopeType == "Cohort" && x.Status == "Active")
            .Join(_db.StudyPlans.AsNoTracking(),
                e => e.PlanVersionId,
                p => p.Id,
                (e, p) => new
                {
                    e.ScopeId,
                    e.EnrolledAt,
                    e.UpdatedAt,
                    StableId = p.StableId == Guid.Empty ? p.Id : p.StableId
                })
            .Where(x => x.StableId == planStableId)
            .OrderByDescending(x => x.UpdatedAt ?? x.EnrolledAt)
            .Select(x => (Guid?)x.ScopeId)
            .FirstOrDefaultAsync(ct);
    }

    private async Task DeactivateOtherPlanEnrollmentsAsync(string userId, Guid planStableId, Guid keepCohortId, CancellationToken ct)
    {
        var others = await _db.StudyPlanEnrollments
            .Where(x => x.UserId == userId && x.ScopeType == "Cohort" && x.ScopeId != keepCohortId && x.Status == "Active")
            .Join(_db.StudyPlans.AsNoTracking(),
                e => e.PlanVersionId,
                p => p.Id,
                (e, p) => new
                {
                    Enrollment = e,
                    StableId = p.StableId == Guid.Empty ? p.Id : p.StableId
                })
            .Where(x => x.StableId == planStableId)
            .Select(x => x.Enrollment)
            .ToListAsync(ct);

        if (others.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var enrollment in others)
        {
            enrollment.Status = "Inactive";
            enrollment.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task UpsertCohortNodeAsync(CohortEntity c)
    {
        var offering = await GetCurrentOfferingAsync(c.Id);
        if (offering == null)
        {
            return;
        }

        var planStableId = await _db.StudyGroupStudyPlans.AsNoTracking()
            .Where(x => x.Id == offering.StudyGroupStudyPlanId)
            .Select(x => x.StudyPlanStableId)
            .FirstOrDefaultAsync();
        if (planStableId == Guid.Empty)
        {
            return;
        }

        var versionNumber = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.Id == offering.StudyPlanVersionId)
            .Select(p => p.VersionNumber)
            .FirstOrDefaultAsync();
        if (versionNumber <= 0)
        {
            return;
        }

        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (p:StudyPlan {id:$planId})
MERGE (c:Cohort {id:$id})
SET c.title=$title, c.visibility=$visibility, c.startAt=$startAt, c.endAt=$endAt
MERGE (c)-[:FOR_PLAN]->(p)
WITH c
OPTIONAL MATCH (c)-[r:OF_VERSION]->(:PlanVersion)
DELETE r
WITH c
MERGE (v:PlanVersion {studyPlanId:$planId, versionNumber:$versionNumber})
MERGE (c)-[:OF_VERSION]->(v)
";
            await tx.RunAsync(cypher, new
            {
                id = c.Id.ToString(),
                planId = planStableId.ToString(),
                title = c.Title,
                visibility = c.Visibility,
                startAt = offering.StartAt,
                endAt = offering.EndAt,
                versionNumber
            });
        });
    }

    public async Task EnsureEnrollmentAsync(string userId, Guid planVersionId, CancellationToken ct = default)
    {
        var (stableId, versionNumber) = await ResolvePlanVersionKeyAsync(planVersionId, ct);
        if (stableId == Guid.Empty || versionNumber <= 0)
        {
            throw new InvalidOperationException("plan_version_not_found");
        }

        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MERGE (pv:PlanVersion {studyPlanId:$planId, versionNumber:$versionNumber})
MERGE (:User {id:$userId})-[:ENROLLED_IN]->(pv)";
            await tx.RunAsync(cypher, new { userId, planId = stableId.ToString(), versionNumber });
        });
    }

    private async Task UpsertStudyPlanEnrollmentAsync(string userId, Guid cohortId, Guid planVersionId, CancellationToken ct)
    {
        var existing = await _db.StudyPlanEnrollments
            .FirstOrDefaultAsync(x => x.UserId == userId && x.ScopeType == "Cohort" && x.ScopeId == cohortId, ct);
        if (existing == null)
        {
            _db.StudyPlanEnrollments.Add(new StudyPlanEnrollment
            {
                UserId = userId,
                ScopeType = "Cohort",
                ScopeId = cohortId,
                PlanVersionId = planVersionId,
                Status = "Active",
                EnrolledAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.PlanVersionId = planVersionId;
            if (!string.Equals(existing.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                existing.Status = "Active";
            }
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task EnsureCohortMembershipAsync(string userId, Guid cohortId, CancellationToken ct = default)
    {
        var existing = await _db.UserGroups
            .FirstOrDefaultAsync(x => x.GroupId == cohortId && x.UserId == userId, ct);
        if (existing == null)
        {
            _db.UserGroups.Add(new UserGroupEntity
            {
                GroupId = cohortId,
                UserId = userId,
                Role = Models.Enums.GroupRole.Member,
                Status = "Active"
            });
            await _db.SaveChangesAsync(ct);
        }
        else if (!string.Equals(existing.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            existing.Status = "Active";
            existing.LeftAt = null;
            await _db.SaveChangesAsync(ct);
        }

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
RETURN any(x IN nums WHERE x <> pv.versionNumber) AS mismatch, pv.studyPlanId AS planId, pv.versionNumber AS versionNumber";
            var cur = await tx.RunAsync(cypher, new { userId, cohortId = cohortId.ToString() });
            return await cur.SingleAsync();
        });
        var mismatch = result["mismatch"].As<bool>();
        var planIdStr = result["planId"].As<string>();
        var versionNumber = result["versionNumber"].As<int>();
        Guid pvId = Guid.Empty;
        if (Guid.TryParse(planIdStr, out var stableId))
        {
            pvId = await ResolvePlanVersionIdAsync(stableId, versionNumber, ct);
        }
        if (pvId != Guid.Empty)
        {
            await UpsertStudyPlanEnrollmentAsync(userId, cohortId, pvId, ct);
        }
        return (mismatch, pvId);
    }

    public async Task AlignEnrollmentVersionAsync(string userId, Guid targetVersionId, CancellationToken ct = default)
    {
        var (stableId, versionNumber) = await ResolvePlanVersionKeyAsync(targetVersionId, ct);
        if (stableId == Guid.Empty || versionNumber <= 0)
        {
            return;
        }

        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (:User {id:$userId})-[r:ENROLLED_IN]->(old:PlanVersion {studyPlanId:$planId})
WHERE old.versionNumber <> $versionNumber
DELETE r
MERGE (pv:PlanVersion {studyPlanId:$planId, versionNumber:$versionNumber})
MERGE (:User {id:$userId})-[:ENROLLED_IN]->(pv)";
            await tx.RunAsync(cypher, new { userId, planId = stableId.ToString(), versionNumber });
        });
    }

    public async Task<int> MigrateProgressAsync(string userId, Guid fromVersionId, Guid toVersionId, CancellationToken ct = default)
    {
        var (fromStableId, fromVersionNo) = await ResolvePlanVersionKeyAsync(fromVersionId, ct);
        var (toStableId, toVersionNo) = await ResolvePlanVersionKeyAsync(toVersionId, ct);
        if (fromStableId == Guid.Empty || toStableId == Guid.Empty || fromVersionNo <= 0 || toVersionNo <= 0)
        {
            return 0;
        }

        await using var session = _driver.AsyncSession();
        return await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (from:PlanVersion {studyPlanId:$fromPlanId, versionNumber:$fromVersionNo})-[:HAS_RESOURCE]->(r:Resource)
WITH collect(r) AS fromSet
MATCH (to:PlanVersion {studyPlanId:$toPlanId, versionNumber:$toVersionNo})-[:HAS_RESOURCE]->(r2:Resource)
WITH fromSet, collect(r2) AS toSet
WITH [x IN fromSet WHERE x IN toSet] AS common
MATCH (u:User {id:$userId})
UNWIND common AS res
MERGE (u)-[c:COMPLETED]->(res)
RETURN count(res) AS migrated";
            var cur = await tx.RunAsync(cypher, new
            {
                userId,
                fromPlanId = fromStableId.ToString(),
                fromVersionNo,
                toPlanId = toStableId.ToString(),
                toVersionNo
            });
            var rec = await cur.SingleAsync();
            return rec["migrated"].As<int>();
        });
    }

    

    public async Task<int> EnsureEnrollmentToCurrentVersionAsync(Guid planId, string userId, CancellationToken ct = default)
    {
        var planEntity = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.Id == planId)
            .Select(p => new { p.StableId, p.VersionNumber, p.IsCurrent })
            .FirstOrDefaultAsync(ct);

        Guid stableId;
        if (planEntity != null)
        {
            stableId = planEntity.StableId == Guid.Empty ? planId : planEntity.StableId;
        }
        else
        {
            stableId = await ResolvePlanStableIdAsync(planId, ct);
            if (stableId == Guid.Empty)
            {
                return 0;
            }
        }

        var versionNumber = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.StableId == stableId)
            .OrderByDescending(p => p.IsCurrent)
            .ThenByDescending(p => p.VersionNumber)
            .Select(p => p.VersionNumber)
            .FirstOrDefaultAsync(ct);

        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MERGE (pv:PlanVersion {studyPlanId:$planId, versionNumber:$versionNumber})
MERGE (u:User {id:$userId})
MERGE (u)-[:ENROLLED_IN]->(pv)";
            await tx.RunAsync(cypher, new { planId = stableId.ToString(), userId, versionNumber });
        });

        return versionNumber;
    }

    private static CohortViewDto ToDto(CohortEntity c, Guid planStableId, CohortOffering? offering)
        => new CohortViewDto(c.Id, planStableId, c.Title, c.Visibility, offering?.StartAt, offering?.EndAt, c.CreatedBy, c.CreatedAt);
}
