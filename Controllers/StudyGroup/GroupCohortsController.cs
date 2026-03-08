using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Sciencetopia.Data;
using Sciencetopia.Models;
using Sciencetopia.Models.Enums;
using Sciencetopia.Services;
using Microsoft.AspNetCore.Authorization;

namespace Sciencetopia.Controllers.StudyGroups;

[ApiController]
[Route("api/Groups/{groupId:guid}")]
public class GroupCohortsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IDriver _driver;
    private readonly StudyGroupService _groups;

    public GroupCohortsController(ApplicationDbContext db, IDriver driver, StudyGroupService groups)
    {
        _db = db;
        _driver = driver;
        _groups = groups;
    }

    private async Task<Dictionary<Guid, int>> GetCohortMemberCountsAsync(List<Guid> cohortIds)
    {
        if (cohortIds == null || cohortIds.Count == 0) return new Dictionary<Guid, int>();

        return await _db.UserGroups.AsNoTracking()
            .Where(ug => cohortIds.Contains(ug.GroupId) && (string.IsNullOrEmpty(ug.Status) || ug.Status == "Active" || ug.Status == "active"))
            .GroupBy(ug => ug.GroupId)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count);
    }

    public class CreateGroupCohortRequest
    {
        public string? Title { get; set; }
        public string? Visibility { get; set; }
        public CohortEnrollMode? EnrollMode { get; set; }
    }

    [HttpGet("CohortPlans")] // B5-4 list group-scoped
    public async Task<IActionResult> List(Guid groupId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var cohorts = await _db.Cohorts.AsNoTracking()
            .Where(c => c.StudyGroupId == groupId)
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
            .ToListAsync();

        var planVersionIds = cohorts.Select(c => c.Offering.StudyPlanVersionId).Distinct().ToList();
        var versionSummaries = await _db.StudyPlans.AsNoTracking()
            .Where(p => planVersionIds.Contains(p.Id))
            .Select(p => new { p.Id, p.StableId, p.VersionNumber, p.IsCurrent, p.Title })
            .ToListAsync();

        var stableIds = cohorts
            .Select(c => c.StudyPlanStableId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var planSummaries = await _db.StudyPlans.AsNoTracking()
            .Where(p => stableIds.Contains(p.StableId == Guid.Empty ? p.Id : p.StableId))
            .Select(p => new { p.Id, p.StableId, p.VersionNumber, p.IsCurrent, p.Title })
            .ToListAsync();

        var currentVersionLookup = planSummaries
            .GroupBy(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.IsCurrent).ThenByDescending(x => x.VersionNumber).First().VersionNumber
            );

        var titleLookup = planSummaries
            .GroupBy(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.VersionNumber).First().Title
            );

        var versionLookup = versionSummaries.ToDictionary(
            v => v.Id,
            v => new
            {
                StableId = v.StableId == Guid.Empty ? v.Id : v.StableId,
                v.VersionNumber,
                v.Title
            });

        var cohortIds = cohorts.Select(c => c.Cohort.Id).ToList();
        var memberCounts = await GetCohortMemberCountsAsync(cohortIds);

        var result = cohorts.Select(c =>
        {
            versionLookup.TryGetValue(c.Offering.StudyPlanVersionId, out var versionInfo);
            var stableId = c.StudyPlanStableId != Guid.Empty ? c.StudyPlanStableId : (versionInfo?.StableId ?? Guid.Empty);
            var title = stableId != Guid.Empty && titleLookup.TryGetValue(stableId, out var t) ? t : versionInfo?.Title;
            var currentNo = stableId != Guid.Empty && currentVersionLookup.TryGetValue(stableId, out var cur) ? cur : (int?)null;
            var pinnedNo = versionInfo?.VersionNumber;
            var memberCount = memberCounts.TryGetValue(c.Cohort.Id, out var cnt) ? cnt : 0;

            return new
            {
                id = c.Cohort.Id,
                studyPlanId = c.Offering.StudyPlanVersionId, // for FE routing
                studyPlanStableId = stableId,
                planTitle = title,
                planCurrentVersionNumber = currentNo,
                title = c.Cohort.Title,
                visibility = c.Cohort.Visibility,
                enrollMode = c.Cohort.EnrollmentPolicy,
                pinnedVersionNumber = pinnedNo,
                memberCount,
                createdAt = c.Cohort.CreatedAt,
                createdBy = c.Cohort.CreatedBy
            };
        });

        return Ok(result);
    }

    [HttpGet("Plans/{planStableId:guid}/Cohorts")]
    public async Task<IActionResult> ListByPlan(Guid groupId, Guid planStableId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var canRead = await _groups.IsUserMemberAsync(groupId.ToString(), userId);
        if (!canRead) return Forbid();

        var cohorts = await _db.Cohorts.AsNoTracking()
            .Where(c => c.StudyGroupId == groupId)
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
            .Where(x => x.StudyPlanStableId == planStableId)
            .OrderByDescending(x => x.Cohort.CreatedAt)
            .ToListAsync();

        var planVersionIds = cohorts.Select(c => c.Offering.StudyPlanVersionId).Distinct().ToList();
        var versions = await _db.StudyPlans.AsNoTracking()
            .Where(p => planVersionIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Title, p.VersionNumber })
            .ToListAsync();
        var versionMap = versions.ToDictionary(x => x.Id, x => x);

        var cohortIds = cohorts.Select(c => c.Cohort.Id).ToList();
        var memberCounts = await GetCohortMemberCountsAsync(cohortIds);

        var result = cohorts.Select(c =>
        {
            versionMap.TryGetValue(c.Offering.StudyPlanVersionId, out var versionInfo);
            var memberCount = memberCounts.TryGetValue(c.Cohort.Id, out var cnt) ? cnt : 0;

            return new
            {
                id = c.Cohort.Id,
                studyGroupId = c.Cohort.StudyGroupId,
                studyPlanStableId = c.StudyPlanStableId,
                studyPlanId = c.Offering.StudyPlanVersionId,
                planTitle = versionInfo?.Title,
                title = c.Cohort.Title,
                visibility = c.Cohort.Visibility,
                enrollMode = c.Cohort.EnrollmentPolicy.ToString(),
                pinnedVersionNumber = versionInfo?.VersionNumber,
                memberCount,
                createdAt = c.Cohort.CreatedAt,
                createdBy = c.Cohort.CreatedBy
            };
        });

        return Ok(result);
    }

    [HttpPost("Plans/{planStableId:guid}/Cohorts")] // B5-4 create group-scoped
    [Authorize(Policy = "Plan.Edit")]
    public async Task<IActionResult> Create(Guid groupId, Guid planStableId, [FromBody] CreateGroupCohortRequest body)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var isManager = await _groups.IsUserManagerAsync(groupId.ToString(), userId);
        if (!isManager) return Forbid();

        var adoption = await _db.StudyGroupStudyPlans.AsNoTracking()
            .FirstOrDefaultAsync(x => x.StudyGroupId == groupId && x.StudyPlanStableId == planStableId);
        if (adoption == null)
        {
            return NotFound(new { message = "Study plan not adopted by this group." });
        }

        StudyPlanEntity? current = null;
        if (adoption.PlanVersionId.HasValue)
        {
            current = await _db.StudyPlans.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == adoption.PlanVersionId.Value);
        }
        if (current == null)
        {
            current = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.StableId == planStableId || (p.StableId == Guid.Empty && p.Id == planStableId))
                .OrderByDescending(p => p.IsCurrent)
                .ThenByDescending(p => p.VersionNumber)
                .FirstOrDefaultAsync();
        }
        if (current == null)
        {
            return NotFound(new { message = "Study plan version not found." });
        }

        var pinnedNo = current.VersionNumber;

        var group = new GroupEntity
        {
            Kind = "Cohort",
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var cohort = new CohortEntity
        {
            Id = group.Id,
            StudyGroupId = groupId,
            Title = body.Title,
            Visibility = string.IsNullOrEmpty(body.Visibility) ? "group" : body.Visibility,
            EnrollmentPolicy = body.EnrollMode ?? CohortEnrollMode.OptIn,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };

        var offering = new CohortOffering
        {
            CohortGroupId = group.Id,
            StudyGroupStudyPlanId = adoption.Id,
            StudyPlanVersionId = current.Id,
            Status = "Active",
            StartAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userId
        };
        cohort.CurrentOfferingId = offering.Id;

        _db.Groups.Add(group);
        _db.Cohorts.Add(cohort);
        _db.CohortOfferings.Add(offering);
        await _db.SaveChangesAsync();

        // Neo4j wiring
        await using (var session = _driver.AsyncSession())
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                var cypher = @"
MATCH (p:StudyPlan {id:$planId})
MERGE (c:Cohort {id:$cohortId})
SET c.title=$title, c.visibility=$visibility
MERGE (c)-[:FOR_PLAN]->(p)
MERGE (g:StudyGroup {id:$groupId})
MERGE (c)-[:SCOPED_BY]->(g)
MERGE (v:PlanVersion {studyPlanId:$planId, versionNumber:$versionNumber})
MERGE (c)-[:OF_VERSION]->(v)
";
                await tx.RunAsync(cypher, new
                {
                    planId = planStableId.ToString(),
                    cohortId = cohort.Id.ToString(),
                    title = cohort.Title,
                    visibility = cohort.Visibility,
                    groupId = groupId.ToString(),
                    versionNumber = pinnedNo
                });
            });
        }

        return Ok(new { cohortId = cohort.Id, pinnedVersionNumber = pinnedNo });
    }

    public class UpdateGroupCohortRequest
    {
        public CohortEnrollMode? EnrollMode { get; set; }
        public int? PinnedVersionNumber { get; set; }
    }

    [HttpPatch("Cohorts/{cohortId:guid}")] // B5-4 patch enrollMode/pinnedVersion
    [Authorize(Policy = "Cohort.Manage")]
    public async Task<IActionResult> Patch(Guid groupId, Guid cohortId, [FromBody] UpdateGroupCohortRequest body)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var isManager = await _groups.IsUserManagerAsync(groupId.ToString(), userId);
        if (!isManager) return Forbid();

        var cohort = await _db.Cohorts.FirstOrDefaultAsync(x => x.Id == cohortId && x.StudyGroupId == groupId);
        if (cohort == null) return NotFound();

        if (body.EnrollMode.HasValue)
        {
            cohort.EnrollmentPolicy = body.EnrollMode.Value;
        }

        int? pinnedNo = null;
        Guid? updatedPlanVersionId = null;
        if (body.PinnedVersionNumber.HasValue)
        {
            var requestedNo = body.PinnedVersionNumber.Value;
            CohortOffering? currentOffering = null;
            if (cohort.CurrentOfferingId.HasValue)
            {
                currentOffering = await _db.CohortOfferings.FirstOrDefaultAsync(o => o.Id == cohort.CurrentOfferingId.Value);
            }
            if (currentOffering == null)
            {
                currentOffering = await _db.CohortOfferings.FirstOrDefaultAsync(o => o.CohortGroupId == cohort.Id && o.Status == "Active");
            }
            if (currentOffering == null)
            {
                return BadRequest(new { code = "offering_not_found" });
            }

            var adoption = await _db.StudyGroupStudyPlans.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == currentOffering.StudyGroupStudyPlanId);
            if (adoption == null)
            {
                return BadRequest(new { code = "adoption_not_found" });
            }

            var planVersion = await _db.StudyPlans.AsNoTracking()
                .Where(p => (p.StableId == adoption.StudyPlanStableId || (p.StableId == Guid.Empty && p.Id == adoption.StudyPlanStableId))
                            && p.VersionNumber == requestedNo)
                .FirstOrDefaultAsync();
            if (planVersion == null)
            {
                return BadRequest(new { code = "version_not_found", versionNumber = requestedNo });
            }

            if (planVersion.Id != currentOffering.StudyPlanVersionId)
            {
                currentOffering.Status = "Archived";
                currentOffering.EndAt = DateTime.UtcNow;

                var nextOffering = new CohortOffering
                {
                    CohortGroupId = cohort.Id,
                    StudyGroupStudyPlanId = currentOffering.StudyGroupStudyPlanId,
                    StudyPlanVersionId = planVersion.Id,
                    Status = "Active",
                    StartAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = userId
                };
                _db.CohortOfferings.Add(nextOffering);
                cohort.CurrentOfferingId = nextOffering.Id;
                updatedPlanVersionId = planVersion.Id;
                pinnedNo = requestedNo;
            }
        }

        await _db.SaveChangesAsync();

        if (pinnedNo.HasValue && updatedPlanVersionId.HasValue)
        {
            await using var session = _driver.AsyncSession();
            await session.ExecuteWriteAsync(async tx =>
            {
                var cypher = @"
MATCH (c:Cohort {id:$cohortId})
OPTIONAL MATCH (c)-[r:OF_VERSION]->(:PlanVersion)
DELETE r
WITH c
MATCH (p:StudyPlan)<-[:FOR_PLAN]-(c)
MERGE (v:PlanVersion {studyPlanId:p.id, versionNumber:$versionNumber})
MERGE (c)-[:OF_VERSION]->(v)
// Align enrolled users in this cohort to the new PlanVersion
OPTIONAL MATCH (u:User)-[:IN_COHORT]->(c)
WITH c, v AS pv, collect(u) AS users
UNWIND users AS u
MERGE (u)-[:ENROLLED_IN]->(pv)";
                await tx.RunAsync(cypher, new
                {
                    cohortId = cohortId.ToString(),
                    versionNumber = pinnedNo.Value
                });
            });

            await AlignCohortEnrollmentsAsync(cohortId, updatedPlanVersionId.Value);
        }

        return Ok();
    }

    [HttpPost("Cohorts/{cohortId:guid}/UpgradeVersion")] // B5-4 upgrade to plan current
    [Authorize(Policy = "Cohort.Manage")]
    public async Task<IActionResult> UpgradeToCurrent(Guid groupId, Guid cohortId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var isManager = await _groups.IsUserManagerAsync(groupId.ToString(), userId);
        if (!isManager) return Forbid();

        var cohort = await _db.Cohorts.FirstOrDefaultAsync(x => x.Id == cohortId && x.StudyGroupId == groupId);
        if (cohort == null) return NotFound();

        CohortOffering? currentOffering = null;
        if (cohort.CurrentOfferingId.HasValue)
        {
            currentOffering = await _db.CohortOfferings.FirstOrDefaultAsync(o => o.Id == cohort.CurrentOfferingId.Value);
        }
        if (currentOffering == null)
        {
            currentOffering = await _db.CohortOfferings.FirstOrDefaultAsync(o => o.CohortGroupId == cohort.Id && o.Status == "Active");
        }
        if (currentOffering == null)
        {
            return BadRequest(new { code = "offering_not_found" });
        }

        var adoption = await _db.StudyGroupStudyPlans.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == currentOffering.StudyGroupStudyPlanId);
        if (adoption == null)
        {
            return BadRequest(new { code = "adoption_not_found" });
        }

        var current = await _db.StudyPlans.AsNoTracking()
            .Where(p => p.StableId == adoption.StudyPlanStableId || (p.StableId == Guid.Empty && p.Id == adoption.StudyPlanStableId))
            .OrderByDescending(p => p.IsCurrent)
            .ThenByDescending(p => p.VersionNumber)
            .FirstOrDefaultAsync();

        if (current == null)
        {
            return BadRequest(new { code = "no_versions" });
        }

        var pinnedNo = current.VersionNumber;

        if (current.Id != currentOffering.StudyPlanVersionId)
        {
            currentOffering.Status = "Archived";
            currentOffering.EndAt = DateTime.UtcNow;

            var nextOffering = new CohortOffering
            {
                CohortGroupId = cohort.Id,
                StudyGroupStudyPlanId = currentOffering.StudyGroupStudyPlanId,
                StudyPlanVersionId = current.Id,
                Status = "Active",
                StartAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId
            };
            _db.CohortOfferings.Add(nextOffering);
            cohort.CurrentOfferingId = nextOffering.Id;
            await _db.SaveChangesAsync();

            await using (var session = _driver.AsyncSession())
            {
                await session.ExecuteWriteAsync(async tx =>
                {
                    var cypher = @"
MATCH (c:Cohort {id:$cohortId})
OPTIONAL MATCH (c)-[r:OF_VERSION]->(:PlanVersion)
DELETE r
WITH c
MATCH (p:StudyPlan)<-[:FOR_PLAN]-(c)
MERGE (v:PlanVersion {studyPlanId:p.id, versionNumber:$versionNumber})
MERGE (c)-[:OF_VERSION]->(v)
// Align enrolled users in this cohort to the new PlanVersion
OPTIONAL MATCH (u:User)-[:IN_COHORT]->(c)
WITH c, v AS pv, collect(u) AS users
UNWIND users AS u
MERGE (u)-[:ENROLLED_IN]->(pv)";
                    await tx.RunAsync(cypher, new { cohortId = cohortId.ToString(), versionNumber = pinnedNo });
                });
            }

            await AlignCohortEnrollmentsAsync(cohortId, current.Id);
        }

        return Ok(new { pinnedVersionNumber = pinnedNo });
    }

    private async Task AlignCohortEnrollmentsAsync(Guid cohortId, Guid planVersionId)
    {
        var userIds = await _db.UserGroups.AsNoTracking()
            .Where(x => x.GroupId == cohortId && x.Status == "Active")
            .Select(x => x.UserId)
            .ToListAsync();
        if (userIds.Count == 0)
        {
            return;
        }

        var existing = await _db.StudyPlanEnrollments
            .Where(x => x.ScopeType == "Cohort" && x.ScopeId == cohortId && userIds.Contains(x.UserId))
            .ToListAsync();
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

        await _db.SaveChangesAsync();
    }
}
