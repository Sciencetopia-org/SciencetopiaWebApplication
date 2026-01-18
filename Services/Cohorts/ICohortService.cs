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
            if (groupId.HasValue && groupId.Value != Guid.Empty)
            {
                var isManager = await _db.UserGroups.AsNoTracking()
                    .AnyAsync(x => x.GroupId == groupId.Value && x.UserId == userId && x.Role >= Models.Enums.GroupRole.Admin, ct);
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
MATCH (c:Cohort {id:$cohortId})-[:OF_VERSION]->(pv:PlanVersion)
MATCH (u)-[r:ENROLLED_IN]->(pv)
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

        var planInfo = await GetCohortPlanInfoAsync(cohortId, ct);
        var planVersionId = planInfo.planVersionId;
        if (planVersionId != Guid.Empty)
        {
            var userIds = await _db.UserGroups.AsNoTracking()
                .Where(x => x.GroupId == groupId && x.Status == "Active")
                .Select(x => x.UserId)
                .ToListAsync(ct);

            if (userIds.Count > 0)
            {
                var existing = await _db.StudyPlanEnrollments
                    .Where(x => x.ScopeType == "Cohort" && x.ScopeId == cohortId && userIds.Contains(x.UserId))
                    .ToListAsync(ct);
                var existingMap = existing.ToDictionary(x => x.UserId, StringComparer.OrdinalIgnoreCase);

                var now = DateTimeOffset.UtcNow;
                foreach (var userId in userIds)
                {
                    if (existingMap.TryGetValue(userId, out var enrollment))
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
                            UserId = userId,
                            ScopeType = "Cohort",
                            ScopeId = cohortId,
                            PlanVersionId = planVersionId,
                            Status = "Active",
                            EnrolledAt = now
                        });
                    }
                }

                await _db.SaveChangesAsync(ct);
            }
        }
        return count;
    }

    public async Task<(Guid planId, Guid cohortId)> JoinCohortAsync(Guid cohortId, string userId, bool shareMetrics = true, CancellationToken ct = default)
    {
        // Validate cohort and group-scope access consistently with Enroll
        var (planId, planVersionId) = await EnsureCohortScopeAccessAsync(cohortId, userId, ct);

        await EnsureCohortMembershipAsync(userId, cohortId, ct);

        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (c:Cohort {id:$cohortId})
MATCH (c)-[:OF_VERSION]->(pv:PlanVersion)
MERGE (:User {id:$userId})-[:ENROLLED_IN]->(pv)
RETURN c.id AS cohortId;";
            await tx.RunAsync(cypher, new { userId, cohortId = cohortId.ToString() });
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
            .AnyAsync(x => x.GroupId == info.studyGroupId.Value && x.UserId == userId, ct);
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
            .AnyAsync(x => x.GroupId == target.studyGroupId.Value && x.UserId == userId, ct);
        if (!isMember) throw new UnauthorizedAccessException("forbidden_not_group_member");

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

        await UpsertStudyPlanEnrollmentAsync(userId, toCohortId, target.planVersionId, ct);

        return (stableId, fromId, toCohortId);
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
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"MATCH (pv:PlanVersion {id:$pvId}) MERGE (:User {id:$userId})-[:ENROLLED_IN]->(pv)";
            await tx.RunAsync(cypher, new { userId, pvId = planVersionId.ToString() });
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
RETURN any(x IN nums WHERE x <> pv.versionNumber) AS mismatch, pv.id AS pvId";
            var cur = await tx.RunAsync(cypher, new { userId, cohortId = cohortId.ToString() });
            return await cur.SingleAsync();
        });
        var mismatch = result["mismatch"].As<bool>();
        var pvIdStr = result["pvId"].As<string>();
        Guid.TryParse(pvIdStr, out var pvId);
        if (pvId != Guid.Empty)
        {
            await UpsertStudyPlanEnrollmentAsync(userId, cohortId, pvId, ct);
        }
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
