using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Sciencetopia.Data;
using Sciencetopia.Models;
using Sciencetopia.Models.Enums;

namespace Sciencetopia.Services.Plans;

public interface IPersonalPlanEnrollmentService
{
    Task<Guid> EnsurePersonalGroupAsync(string userId, CancellationToken ct = default);
    Task<Guid> EnsurePersonalGroupProjectionAsync(string userId, CancellationToken ct = default);
    Task EnsureEnrollmentAsync(string userId, Guid planStableId, Guid planVersionId, CancellationToken ct = default);
    Task EnsureEnrollmentAsync(string userId, Guid planStableId, Guid planVersionId, PlanRole role, CancellationToken ct = default);
}

public class PersonalPlanEnrollmentService : IPersonalPlanEnrollmentService
{
    private readonly ApplicationDbContext _db;
    private readonly IDriver _driver;

    public PersonalPlanEnrollmentService(ApplicationDbContext db, IDriver driver)
    {
        _db = db;
        _driver = driver;
    }

    public async Task<Guid> EnsurePersonalGroupAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Guid.Empty;
        }

        var personalGroupId = await _db.UserGroups.AsNoTracking()
            .Where(ug => ug.UserId == userId && ug.Status == "Active")
            .Join(_db.Groups.AsNoTracking(),
                ug => ug.GroupId,
                g => g.Id,
                (ug, g) => new { ug.GroupId, g.Kind, g.CreatedByUserId })
            .Where(x => x.Kind == "PersonalGroup" && x.CreatedByUserId == userId)
            .Select(x => x.GroupId)
            .FirstOrDefaultAsync(ct);

        if (personalGroupId != Guid.Empty)
        {
            return personalGroupId;
        }

        var group = new GroupEntity
        {
            Id = Guid.NewGuid(),
            Kind = "PersonalGroup",
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _db.Groups.Add(group);
        _db.UserGroups.Add(new UserGroupEntity
        {
            GroupId = group.Id,
            UserId = userId,
            Role = GroupRole.Owner,
            Status = "Active",
            JoinedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        return group.Id;
    }

    public async Task<Guid> EnsurePersonalGroupProjectionAsync(string userId, CancellationToken ct = default)
    {
        var personalGroupId = await EnsurePersonalGroupAsync(userId, ct);
        if (personalGroupId == Guid.Empty)
        {
            return Guid.Empty;
        }

        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(@"
MERGE (u:User {id:$userId})
MERGE (g:Group {id:$groupId})
SET g.kind = 'PersonalGroup'
MERGE (u)-[:MEMBER_OF]->(g)", new { userId, groupId = personalGroupId.ToString() });
        });

        return personalGroupId;
    }

    public async Task EnsureEnrollmentAsync(string userId, Guid planStableId, Guid planVersionId, CancellationToken ct = default)
        => await EnsureEnrollmentAsync(userId, planStableId, planVersionId, PlanRole.Viewer, ct);

    public async Task EnsureEnrollmentAsync(string userId, Guid planStableId, Guid planVersionId, PlanRole role, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || planStableId == Guid.Empty || planVersionId == Guid.Empty)
        {
            return;
        }

        var personalGroupId = await EnsurePersonalGroupProjectionAsync(userId, ct);
        if (personalGroupId == Guid.Empty)
        {
            return;
        }

        var existing = await _db.GroupPlanEnrollments
            .FirstOrDefaultAsync(x =>
                x.GroupId == personalGroupId
                && x.StudyPlanStableId == planStableId
                && x.Status == "Active", ct);

        if (existing == null)
        {
            _db.GroupPlanEnrollments.Add(new GroupPlanEnrollment
            {
                GroupId = personalGroupId,
                StudyPlanStableId = planStableId,
                PlanVersionId = planVersionId,
                VersionPolicy = "Current",
                Status = "Active",
                Role = role,
                CreatedBy = userId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.PlanVersionId = planVersionId;
            existing.VersionPolicy = "Current";
            if (role > existing.Role)
            {
                existing.Role = role;
            }
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }
}
