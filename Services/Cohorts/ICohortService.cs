using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Sciencetopia.Data;
using Sciencetopia.DTOs;
using Sciencetopia.Models;
using Sciencetopia.Services.Plans;

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
    Task<(Guid planId, Guid cohortId)> JoinCohortAsync(Guid cohortId, string userId, CancellationToken ct = default);
    // B3-2 SwitchCohortAsync
    Task<(Guid planId, Guid? fromCohortId, Guid toCohortId)> SwitchCohortAsync(Guid planId, Guid toCohortId, string userId, string? migrationStrategy = null, CancellationToken ct = default);

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
    private readonly IPersonalPlanEnrollmentService _personalEnrollment;

    public CohortService(ApplicationDbContext db, IDriver driver, IPersonalPlanEnrollmentService personalEnrollment)
    {
        _db = db;
        _driver = driver;
        _personalEnrollment = personalEnrollment;
    }

    private sealed record CohortPlanInfo(Guid PlanStableId, Guid PlanVersionId, Guid StudyGroupId, int VersionNumber);

    private async Task<Guid> ResolvePlanStableIdAsync(Guid identifier, CancellationToken ct = default)
    {
        var stable = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.Id == identifier)
            .Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
            .FirstOrDefaultAsync(ct);
        if (stable != Guid.Empty) return stable;

        var exists = await _db.StudyPlans.AsNoTracking().AnyAsync(p => p.StableId == identifier, ct);
        return exists ? identifier : Guid.Empty;
    }

    private async Task<(Guid stableId, int versionNumber)> ResolvePlanVersionKeyAsync(Guid versionId, CancellationToken ct = default)
    {
        var key = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.Id == versionId)
            .Select(p => new { StableId = p.StableId == Guid.Empty ? p.Id : p.StableId, p.VersionNumber })
            .FirstOrDefaultAsync(ct);
        return key == null ? (Guid.Empty, 0) : (key.StableId, key.VersionNumber);
    }

    private async Task<Guid> ResolvePlanVersionIdAsync(Guid stableId, int versionNumber, CancellationToken ct = default)
    {
        return await _db.StudyPlans.AsNoTracking()
            .Where(p => (p.StableId == stableId || p.Id == stableId) && p.VersionNumber == versionNumber)
            .Select(p => p.Id)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<CohortPlanInfo?> GetCohortPlanInfoAsync(Guid cohortId, CancellationToken ct = default)
    {
        return await _db.Cohorts.AsNoTracking()
            .Where(c => c.Id == cohortId)
            .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                c => c.StudyGroupStudyPlanId,
                sgsp => sgsp.Id,
                (c, sgsp) => new { Cohort = c, Adoption = sgsp })
            .Join(_db.StudyPlans.AsNoTracking(),
                x => x.Cohort.StudyPlanVersionId,
                p => p.Id,
                (x, p) => new CohortPlanInfo(
                    x.Adoption.StudyPlanStableId,
                    x.Cohort.StudyPlanVersionId,
                    x.Adoption.StudyGroupId,
                    p.VersionNumber))
            .FirstOrDefaultAsync(ct);
    }

    private static string ToGroupRoleLabel(Sciencetopia.Models.Enums.GroupRole role)
    {
        if (role == Sciencetopia.Models.Enums.GroupRole.Owner) return "Owner";
        if (role >= Sciencetopia.Models.Enums.GroupRole.Admin) return "Admin";
        return "Member";
    }

    public async Task<EnrollmentMeDto> GetEnrollmentForUserAsync(Guid planId, string userId, CancellationToken ct = default)
    {
        var dto = new EnrollmentMeDto();
        var stableId = await ResolvePlanStableIdAsync(planId, ct);
        if (stableId == Guid.Empty) return dto;

        var rows = await _db.UserGroups.AsNoTracking()
            .Where(x => x.UserId == userId && x.Status == "Active")
            .Join(_db.Cohorts.AsNoTracking(),
                ug => ug.GroupId,
                c => c.Id,
                (ug, c) => new { Membership = ug, Cohort = c })
            .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                x => x.Cohort.StudyGroupStudyPlanId,
                sgsp => sgsp.Id,
                (x, sgsp) => new { x.Membership, x.Cohort, sgsp.StudyPlanStableId, sgsp.StudyGroupId })
            .Where(x => x.StudyPlanStableId == stableId)
            .ToListAsync(ct);

        var active = rows.OrderByDescending(x => x.Membership.JoinedAt).FirstOrDefault();
        if (active == null) return dto;

        dto.ActiveCohortId = active.Cohort.Id;
        dto.JoinedAt = active.Membership.JoinedAt.ToUnixTimeMilliseconds();
        dto.ArchivedCohortIds = rows
            .Select(x => x.Cohort.Id)
            .Where(id => id != dto.ActiveCohortId.Value)
            .Distinct()
            .ToList();

        var role = await _db.UserGroups.AsNoTracking()
            .Where(x => x.GroupId == active.StudyGroupId && x.UserId == userId && x.Status == "Active")
            .Select(x => (Sciencetopia.Models.Enums.GroupRole?)x.Role)
            .FirstOrDefaultAsync(ct);
        if (role.HasValue) dto.Role = ToGroupRoleLabel(role.Value);

        return dto;
    }

    public async Task<CohortViewDto?> UpdateAsync(Guid cohortId, CohortUpdateDto dto)
    {
        var entity = await _db.Cohorts.FirstOrDefaultAsync(x => x.Id == cohortId);
        if (entity == null) return null;

        if (dto.Title != null) entity.Title = dto.Title;
        if (dto.Visibility != null) entity.Visibility = dto.Visibility;
        if (dto.StartAt.HasValue) entity.StartAt = dto.StartAt.Value;
        entity.EndAt = dto.EndAt;

        await _db.SaveChangesAsync();
        await UpsertCohortNodeAsync(entity);

        var info = await GetCohortPlanInfoAsync(entity.Id);
        return ToDto(entity, info);
    }

    public async Task<List<CohortViewDto>> ListByPlanAsync(Guid planId)
    {
        var stableId = await ResolvePlanStableIdAsync(planId);
        if (stableId == Guid.Empty) return new List<CohortViewDto>();

        var rows = await _db.Cohorts.AsNoTracking()
            .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                c => c.StudyGroupStudyPlanId,
                sgsp => sgsp.Id,
                (c, sgsp) => new { Cohort = c, Adoption = sgsp })
            .Join(_db.StudyPlans.AsNoTracking(),
                x => x.Cohort.StudyPlanVersionId,
                p => p.Id,
                (x, p) => new { x.Cohort, x.Adoption, Version = p })
            .Where(x => x.Adoption.StudyPlanStableId == stableId && x.Cohort.Status == "Active")
            .OrderBy(x => x.Cohort.StartAt)
            .ToListAsync();

        var cohortIds = rows.Select(x => x.Cohort.Id).Distinct().ToList();
        var memberCounts = await GetMemberCountsAsync(cohortIds);

        return rows.Select(x =>
        {
            memberCounts.TryGetValue(x.Cohort.Id, out var memberCount);
            return ToDto(x.Cohort, new CohortPlanInfo(x.Adoption.StudyPlanStableId, x.Cohort.StudyPlanVersionId, x.Adoption.StudyGroupId, x.Version.VersionNumber), memberCount);
        }).ToList();
    }

    public async Task<Guid?> GetPlanIdAsync(Guid cohortId)
    {
        var info = await GetCohortPlanInfoAsync(cohortId);
        return info?.PlanStableId == Guid.Empty ? null : info?.PlanStableId;
    }

    public async Task DeleteAsync(Guid cohortId, CancellationToken ct = default)
    {
        var entity = await _db.Cohorts.FirstOrDefaultAsync(x => x.Id == cohortId, ct);
        if (entity != null)
        {
            _db.Cohorts.Remove(entity);
            var group = await _db.Groups.FindAsync(new object?[] { entity.Id }, ct);
            if (group != null) _db.Groups.Remove(group);
            await _db.SaveChangesAsync(ct);
        }

        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(@"MATCH (c:Cohort {id:$id}) DETACH DELETE c", new { id = cohortId.ToString() });
        });
    }

    public async Task UnenrollAsync(Guid cohortId, string userId)
    {
        var personalGroupId = await _personalEnrollment.EnsurePersonalGroupProjectionAsync(userId);
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (u:User {id:$userId})
OPTIONAL MATCH (u)-[ic:IN_COHORT]->(:Cohort {id:$cohortId})
DELETE ic
WITH u
OPTIONAL MATCH (pg:Group {id:$personalGroupId})
OPTIONAL MATCH (:Cohort {id:$cohortId})-[:OF_VERSION]->(pv:PlanVersion)
OPTIONAL MATCH (pg)-[r:ENROLLED_IN]->(pv)
DELETE r";
            await tx.RunAsync(cypher, new { userId, personalGroupId = personalGroupId.ToString(), cohortId = cohortId.ToString() });
        });

        var membership = await _db.UserGroups.FirstOrDefaultAsync(x => x.GroupId == cohortId && x.UserId == userId);
        if (membership != null)
        {
            _db.UserGroups.Remove(membership);
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
SET r.enabled=$enabled, r.createdAt = coalesce(r.createdAt, datetime())";
            await tx.RunAsync(cypher, new { cohortId = cohortId.ToString(), groupId = groupId.ToString(), enabled });
        });
    }

    public async Task RemoveAutoEnrollGroupAsync(Guid cohortId, Guid groupId)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(@"MATCH (g:StudyGroup {id:$groupId})-[r:AUTO_ENROLL]->(c:Cohort {id:$cohortId}) DELETE r",
                new { cohortId = cohortId.ToString(), groupId = groupId.ToString() });
        });
    }

    public async Task<int> RunAutoEnrollAsync(Guid cohortId, Guid groupId, CancellationToken ct = default)
    {
        var info = await GetCohortPlanInfoAsync(cohortId, ct);
        if (info == null) return 0;

        var userIds = await _db.UserGroups.AsNoTracking()
            .Where(x => x.GroupId == groupId && x.Status == "Active")
            .Select(x => x.UserId)
            .ToListAsync(ct);
        if (userIds.Count == 0) return 0;

        var count = await EnrollUsersInGraphAsync(userIds, cohortId, info.PlanStableId, info.PlanVersionId, info.VersionNumber, ct);
        await UpsertCohortMembershipsAsync(userIds, cohortId, ct);
        return count;
    }

    public async Task<(Guid planId, Guid cohortId)> JoinCohortAsync(Guid cohortId, string userId, CancellationToken ct = default)
    {
        var info = await EnsureCohortScopeAccessAsync(cohortId, userId, ct);
        var existingActive = await GetActiveCohortForPlanAsync(userId, info.PlanStableId, ct);
        if (existingActive.HasValue && existingActive.Value != cohortId) throw new InvalidOperationException("alreadyInPlan");

        await EnsureCohortMembershipAsync(userId, cohortId, ct);
        await _personalEnrollment.EnsureEnrollmentAsync(userId, info.PlanStableId, info.PlanVersionId, ct);
        await DeactivateOtherPlanEnrollmentsAsync(userId, info.PlanStableId, cohortId, ct);
        await EnrollUserInCohortGraphAsync(userId, cohortId, info.PlanStableId);

        return (info.PlanStableId, cohortId);
    }

    public async Task<(Guid planId, Guid? fromCohortId, Guid toCohortId)> SwitchCohortAsync(Guid planId, Guid toCohortId, string userId, string? migrationStrategy = null, CancellationToken ct = default)
    {
        var stableId = await ResolvePlanStableIdAsync(planId, ct);
        var target = await EnsureCohortScopeAccessAsync(toCohortId, userId, ct);
        if (stableId == Guid.Empty || target.PlanStableId != stableId) throw new InvalidOperationException("cohort_plan_mismatch");

        var fromId = await GetActiveCohortForPlanAsync(userId, stableId, ct);
        await EnsureCohortMembershipAsync(userId, toCohortId, ct);
        await _personalEnrollment.EnsureEnrollmentAsync(userId, target.PlanStableId, target.PlanVersionId, ct);
        await DeactivateOtherPlanEnrollmentsAsync(userId, stableId, toCohortId, ct);
        await EnrollUserInCohortGraphAsync(userId, toCohortId, stableId);

        return (stableId, fromId, toCohortId);
    }

    private async Task<CohortPlanInfo> EnsureCohortScopeAccessAsync(Guid cohortId, string userId, CancellationToken ct)
    {
        var info = await GetCohortPlanInfoAsync(cohortId, ct);
        if (info == null) throw new KeyNotFoundException("cohort_not_found");

        var isMember = await _db.UserGroups.AsNoTracking()
            .AnyAsync(x => x.GroupId == info.StudyGroupId && x.UserId == userId && x.Status == "Active", ct);
        if (!isMember) throw new UnauthorizedAccessException("forbidden_not_group_member");

        return info;
    }

    private async Task<Guid?> GetActiveCohortForPlanAsync(string userId, Guid planStableId, CancellationToken ct)
    {
        return await _db.UserGroups.AsNoTracking()
            .Where(x => x.UserId == userId && x.Status == "Active")
            .Join(_db.Cohorts.AsNoTracking(),
                ug => ug.GroupId,
                c => c.Id,
                (ug, c) => new { Membership = ug, Cohort = c })
            .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                x => x.Cohort.StudyGroupStudyPlanId,
                sgsp => sgsp.Id,
                (x, sgsp) => new { x.Cohort.Id, x.Membership.JoinedAt, sgsp.StudyPlanStableId })
            .Where(x => x.StudyPlanStableId == planStableId)
            .OrderByDescending(x => x.JoinedAt)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);
    }

    private async Task DeactivateOtherPlanEnrollmentsAsync(string userId, Guid planStableId, Guid keepCohortId, CancellationToken ct)
    {
        var others = await _db.UserGroups
            .Where(x => x.UserId == userId && x.Status == "Active" && x.GroupId != keepCohortId)
            .Join(_db.Cohorts.AsNoTracking(),
                ug => ug.GroupId,
                c => c.Id,
                (ug, c) => new { Membership = ug, Cohort = c })
            .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                x => x.Cohort.StudyGroupStudyPlanId,
                sgsp => sgsp.Id,
                (x, sgsp) => new { x.Membership, sgsp.StudyPlanStableId })
            .Where(x => x.StudyPlanStableId == planStableId)
            .Select(x => x.Membership)
            .ToListAsync(ct);

        foreach (var membership in others)
        {
            membership.Status = "Inactive";
            membership.LeftAt = DateTimeOffset.UtcNow;
        }

        if (others.Count > 0) await _db.SaveChangesAsync(ct);
    }

    private async Task UpsertCohortNodeAsync(CohortEntity c)
    {
        var info = await GetCohortPlanInfoAsync(c.Id);
        if (info == null) return;

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
MERGE (c)-[:OF_VERSION]->(v)";
            await tx.RunAsync(cypher, new
            {
                id = c.Id.ToString(),
                planId = info.PlanStableId.ToString(),
                title = c.Title,
                visibility = c.Visibility,
                startAt = c.StartAt,
                endAt = c.EndAt,
                versionNumber = info.VersionNumber
            });
        });
    }

    public async Task EnsureEnrollmentAsync(string userId, Guid planVersionId, CancellationToken ct = default)
    {
        var (stableId, versionNumber) = await ResolvePlanVersionKeyAsync(planVersionId, ct);
        if (stableId == Guid.Empty || versionNumber <= 0) throw new InvalidOperationException("plan_version_not_found");

        await _personalEnrollment.EnsureEnrollmentAsync(userId, stableId, planVersionId, ct);
        var personalGroupId = await _personalEnrollment.EnsurePersonalGroupProjectionAsync(userId, ct);

        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(@"
MERGE (pv:PlanVersion {studyPlanId:$planId, versionNumber:$versionNumber})
MERGE (pg:Group {id:$personalGroupId})
SET pg.kind = 'PersonalGroup'
MERGE (pg)-[:ENROLLED_IN]->(pv)", new { personalGroupId = personalGroupId.ToString(), planId = stableId.ToString(), versionNumber });
        });
    }

    public async Task EnsureCohortMembershipAsync(string userId, Guid cohortId, CancellationToken ct = default)
    {
        var existing = await _db.UserGroups.FirstOrDefaultAsync(x => x.GroupId == cohortId && x.UserId == userId, ct);
        if (existing == null)
        {
            _db.UserGroups.Add(new UserGroupEntity
            {
                GroupId = cohortId,
                UserId = userId,
                Role = Sciencetopia.Models.Enums.GroupRole.Member,
                Status = "Active",
                JoinedAt = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }
        else if (!string.Equals(existing.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            existing.Status = "Active";
            existing.LeftAt = null;
            await _db.SaveChangesAsync(ct);
        }

        var personalGroupId = await _personalEnrollment.EnsurePersonalGroupProjectionAsync(userId, ct);
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(@"
MERGE (u:User {id:$userId})
MERGE (pg:Group {id:$personalGroupId})
SET pg.kind = 'PersonalGroup'
MERGE (u)-[:MEMBER_OF]->(pg)
MERGE (c:Cohort {id:$cohortId})
MERGE (u)-[:IN_COHORT]->(c)",
                new { userId, personalGroupId = personalGroupId.ToString(), cohortId = cohortId.ToString() });
        });
    }

    public async Task<(bool versionMismatch, Guid planVersionId)> AutoEnrollToCohortAsync(string userId, Guid cohortId, CancellationToken ct = default)
    {
        var info = await GetCohortPlanInfoAsync(cohortId, ct);
        if (info == null) return (false, Guid.Empty);
        await EnsureCohortMembershipAsync(userId, cohortId, ct);
        await _personalEnrollment.EnsureEnrollmentAsync(userId, info.PlanStableId, info.PlanVersionId, ct);
        await EnrollUserInCohortGraphAsync(userId, cohortId, info.PlanStableId);
        return (false, info.PlanVersionId);
    }

    public async Task AlignEnrollmentVersionAsync(string userId, Guid targetVersionId, CancellationToken ct = default)
    {
        var (stableId, versionNumber) = await ResolvePlanVersionKeyAsync(targetVersionId, ct);
        if (stableId == Guid.Empty || versionNumber <= 0) return;
        var personalGroupId = await _personalEnrollment.EnsurePersonalGroupProjectionAsync(userId, ct);

        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(@"
MATCH (:Group {id:$personalGroupId})-[r:ENROLLED_IN]->(old:PlanVersion {studyPlanId:$planId})
WHERE old.versionNumber <> $versionNumber
DELETE r
MERGE (pv:PlanVersion {studyPlanId:$planId, versionNumber:$versionNumber})
MERGE (:Group {id:$personalGroupId})-[:ENROLLED_IN]->(pv)", new { personalGroupId = personalGroupId.ToString(), planId = stableId.ToString(), versionNumber });
        });
    }

    public async Task<int> MigrateProgressAsync(string userId, Guid fromVersionId, Guid toVersionId, CancellationToken ct = default)
    {
        var (fromStableId, fromVersionNo) = await ResolvePlanVersionKeyAsync(fromVersionId, ct);
        var (toStableId, toVersionNo) = await ResolvePlanVersionKeyAsync(toVersionId, ct);
        if (fromStableId == Guid.Empty || toStableId == Guid.Empty || fromVersionNo <= 0 || toVersionNo <= 0) return 0;
        var personalGroupId = await _personalEnrollment.EnsurePersonalGroupProjectionAsync(userId, ct);

        await using var session = _driver.AsyncSession();
        return await session.ExecuteWriteAsync(async tx =>
        {
            var cur = await tx.RunAsync(@"
MATCH (from:PlanVersion {studyPlanId:$fromPlanId, versionNumber:$fromVersionNo})-[:HAS_RESOURCE]->(r:Resource)
WITH collect(r) AS fromSet
MATCH (to:PlanVersion {studyPlanId:$toPlanId, versionNumber:$toVersionNo})-[:HAS_RESOURCE]->(r2:Resource)
WITH fromSet, collect(r2) AS toSet
WITH [x IN fromSet WHERE x IN toSet] AS common
MATCH (u:Group {id:$personalGroupId})
UNWIND common AS res
MERGE (u)-[c:COMPLETED]->(res)
RETURN count(res) AS migrated", new
            {
                personalGroupId = personalGroupId.ToString(),
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
        var stableId = await ResolvePlanStableIdAsync(planId, ct);
        if (stableId == Guid.Empty) return 0;

        var current = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.StableId == stableId || p.Id == stableId)
            .OrderByDescending(p => p.IsCurrent)
            .ThenByDescending(p => p.VersionNumber)
            .Select(p => new { p.Id, p.VersionNumber })
            .FirstOrDefaultAsync(ct);
        if (current == null) return 0;

        await _personalEnrollment.EnsureEnrollmentAsync(userId, stableId, current.Id, ct);
        return current.VersionNumber;
    }

    private async Task<int> EnrollUsersInGraphAsync(List<string> userIds, Guid cohortId, Guid stableId, Guid planVersionId, int versionNumber, CancellationToken ct)
    {
        var personalGroupRows = new List<object>();
        foreach (var uid in userIds)
        {
            await _personalEnrollment.EnsureEnrollmentAsync(uid, stableId, planVersionId, ct);
            var personalGroupId = await _personalEnrollment.EnsurePersonalGroupProjectionAsync(uid);
            if (personalGroupId != Guid.Empty)
            {
                personalGroupRows.Add(new { userId = uid, groupId = personalGroupId.ToString() });
            }
        }

        await using var session = _driver.AsyncSession();
        return await session.ExecuteWriteAsync(async tx =>
        {
            var cursor = await tx.RunAsync(@"
MERGE (pv:PlanVersion {studyPlanId:$planId, versionNumber:$versionNumber})
MERGE (c:Cohort {id:$cohortId})
UNWIND $personalGroups AS row
MERGE (u:User {id:row.userId})
MERGE (pg:Group {id:row.groupId})
SET pg.kind = 'PersonalGroup'
MERGE (u)-[:MEMBER_OF]->(pg)
MERGE (u)-[:IN_COHORT]->(c)
MERGE (pg)-[:ENROLLED_IN]->(pv)
RETURN count(DISTINCT row.userId) AS processed", new { cohortId = cohortId.ToString(), planId = stableId.ToString(), versionNumber, personalGroups = personalGroupRows });
            var rec = await cursor.SingleAsync();
            return rec["processed"].As<int>();
        });
    }

    private async Task EnrollUserInCohortGraphAsync(string userId, Guid cohortId, Guid stableId)
    {
        var personalGroupId = await _personalEnrollment.EnsurePersonalGroupProjectionAsync(userId);
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(@"
MATCH (c:Cohort {id:$cohortId})
MATCH (c)-[:OF_VERSION]->(pv:PlanVersion)
MERGE (u:User {id:$userId})
MERGE (pg:Group {id:$personalGroupId})
SET pg.kind = 'PersonalGroup'
MERGE (u)-[:MEMBER_OF]->(pg)
OPTIONAL MATCH (u)-[oldIn:IN_COHORT]->(:Cohort)-[:FOR_PLAN]->(:StudyPlan {id:$planId})
DELETE oldIn
WITH u, pg, c, pv
OPTIONAL MATCH (pg)-[oldEn:ENROLLED_IN]->(:PlanVersion {studyPlanId:$planId})
DELETE oldEn
MERGE (u)-[:IN_COHORT]->(c)
MERGE (pg)-[:ENROLLED_IN]->(pv)", new { userId, personalGroupId = personalGroupId.ToString(), cohortId = cohortId.ToString(), planId = stableId.ToString() });
        });
    }

    private async Task UpsertCohortMembershipsAsync(List<string> userIds, Guid cohortId, CancellationToken ct)
    {
        var existingMembers = await _db.UserGroups
            .Where(x => x.GroupId == cohortId && userIds.Contains(x.UserId))
            .ToListAsync(ct);
        var existingMap = existingMembers.ToDictionary(x => x.UserId, StringComparer.OrdinalIgnoreCase);

        foreach (var uid in userIds)
        {
            if (existingMap.TryGetValue(uid, out var membership))
            {
                membership.Status = "Active";
                membership.LeftAt = null;
            }
            else
            {
                _db.UserGroups.Add(new UserGroupEntity
                {
                    UserId = uid,
                    GroupId = cohortId,
                    Role = Sciencetopia.Models.Enums.GroupRole.Member,
                    Status = "Active",
                    JoinedAt = DateTimeOffset.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<Guid, int>> GetMemberCountsAsync(List<Guid> cohortIds)
    {
        if (cohortIds.Count == 0) return new Dictionary<Guid, int>();
        return await _db.UserGroups.AsNoTracking()
            .Where(x => cohortIds.Contains(x.GroupId) && (string.IsNullOrEmpty(x.Status) || x.Status == "Active" || x.Status == "active"))
            .GroupBy(x => x.GroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count);
    }

    private static CohortViewDto ToDto(CohortEntity c, CohortPlanInfo? info, int? memberCount = null)
        => new(
            c.Id,
            info?.PlanStableId ?? Guid.Empty,
            c.Title,
            c.Visibility,
            c.StartAt,
            c.EndAt,
            c.CreatedBy,
            c.CreatedAt,
            info?.PlanVersionId,
            info?.StudyGroupId,
            info?.VersionNumber,
            c.EnrollmentPolicy.ToString(),
            memberCount);
}


