using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sciencetopia.Data;
using Sciencetopia.Services;
using Sciencetopia.Services.ContentSafety;
using Sciencetopia.Models;

namespace Sciencetopia.Controllers.StudyGroups;

[ApiController]
[Route("api/[controller]")]
public class StudyGroupController : ControllerBase
{
    private sealed class SettingsCohortRow
    {
        public Guid Id { get; set; }
        public string? Title { get; set; }
        public object? EnrollmentPolicy { get; set; }
        public Guid StudyPlanVersionId { get; set; }
        public Guid StudyPlanStableId { get; set; }
        public int? PinnedVersionNumber { get; set; }
    }

    private sealed class SettingsMyCohortPlanRow
    {
        public Guid CohortId { get; set; }
        public DateTimeOffset JoinedAt { get; set; }
        public Guid PlanVersionId { get; set; }
        public Guid PlanStableId { get; set; }
        public string? CohortTitle { get; set; }
    }

    // Dependency injection for database context
    private readonly StudyGroupService _studyGroupService;
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IContentModerationService _contentModeration;

    public StudyGroupController(StudyGroupService studyGroupService, ApplicationDbContext db, IMemoryCache cache, IContentModerationService contentModeration)
    {
        _studyGroupService = studyGroupService;
        _db = db;
        _cache = cache;
        _contentModeration = contentModeration;
    }

    [HttpGet("GetAllStudyGroups")]
    public async Task<ActionResult<IEnumerable<StudyGroup>>> GetAllStudyGroups()
    {
        var groups = await _studyGroupService.GetAllStudyGroups();
        return Ok(groups);
    }

    [HttpGet("List")]
    public async Task<ActionResult<IEnumerable<StudyGroup>>> List(int page = 1, int pageSize = 20)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
            return BadRequest("Invalid pagination parameters.");
        var skip = (page - 1) * pageSize;
        var groups = await _studyGroupService.GetStudyGroupsPagedAsync(skip, pageSize);
        return Ok(groups);
    }

    [HttpGet("GetStudyGroupById/{groupId}")]
    public async Task<ActionResult<StudyGroup>> GetStudyGroupById(string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        var group = await _studyGroupService.GetStudyGroupByIdAsync(groupId);
        if (group == null)
        {
            return NotFound("Study group not found.");
        }
        return Ok(group);
    }

    [HttpGet("Bootstrap/{groupId}")]
    public async Task<IActionResult> GetBootstrap(string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }

        string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var cacheKey = $"studygroup:bootstrap:{groupId}:{userId}";
        if (_cache.TryGetValue<object>(cacheKey, out var cachedBootstrap) && cachedBootstrap != null)
        {
            return Ok(cachedBootstrap);
        }

        // These service calls share scoped EF dependencies; keep them serialized to avoid
        // nondeterministic first-load state or DbContext concurrency issues.
        var group = await _studyGroupService.GetStudyGroupPreviewByIdAsync(groupId, memberLimit: 8);
        if (group == null)
        {
            return NotFound("Study group not found.");
        }

        var tags = await _studyGroupService.GetGroupTagsAsync(groupId);
        var role = string.IsNullOrEmpty(userId)
            ? string.Empty
            : await _studyGroupService.GetUserRoleInGroupAsync(groupId, userId) ?? string.Empty;
        var isMember = !string.IsNullOrEmpty(role);
        var pendingJoinRequests = 0;
        if (!string.IsNullOrEmpty(userId)
            && (role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Manager", StringComparison.OrdinalIgnoreCase)))
        {
            pendingJoinRequests = await _studyGroupService.GetPendingJoinRequestsCount(groupId);
        }

        var payload = new
        {
            group,
            role,
            isMember,
            tags,
            pendingJoinRequests
        };

        _cache.Set(cacheKey, payload, TimeSpan.FromSeconds(30));
        return Ok(payload);
    }

    [HttpGet("SettingsBootstrap/{groupId}")]
    [Authorize]
    public async Task<IActionResult> GetSettingsBootstrap(string groupId)
    {
        if (!Guid.TryParse(groupId, out var gid))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }

        var userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("User is not authenticated.");
        }

        var groupTask = _studyGroupService.GetStudyGroupByIdAsync(groupId);
        Task<string?> roleTask = _studyGroupService.GetUserRoleInGroupAsync(groupId, userId);

        await Task.WhenAll(groupTask, roleTask);

        var group = groupTask.Result;
        if (group == null)
        {
            return NotFound("Study group not found.");
        }

        var role = roleTask.Result ?? string.Empty;
        if (string.IsNullOrEmpty(role))
        {
            return Forbid();
        }

        var sharedPlans = await _db.StudyGroupStudyPlans.AsNoTracking()
            .Where(x => x.StudyGroupId == gid)
            .Select(x => new
            {
                x.StudyPlanStableId,
                x.PlanVersionId,
                x.PinnedVersionNumber,
                x.Permission,
                x.AutoEnroll
            })
            .ToListAsync();

        var planStableIds = sharedPlans.Select(x => x.StudyPlanStableId).Where(x => x != Guid.Empty).Distinct().ToList();
        var planVersionIds = sharedPlans.Where(x => x.PlanVersionId.HasValue).Select(x => x.PlanVersionId!.Value).Distinct().ToList();
        var relevantPlanByIdFallbacks = planStableIds;

        var relevantPlans = await _db.StudyPlans.AsNoTracking()
            .Where(p =>
                planVersionIds.Contains(p.Id)
                || planStableIds.Contains(p.StableId)
                || (p.StableId == Guid.Empty && relevantPlanByIdFallbacks.Contains(p.Id)))
            .Select(p => new
            {
                p.Id,
                StableId = p.StableId == Guid.Empty ? p.Id : p.StableId,
                p.VersionNumber,
                p.IsCurrent,
                p.Title
            })
            .ToListAsync();

        var sharedPlanTitleLookup = relevantPlans
            .GroupBy(p => p.StableId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.IsCurrent).ThenByDescending(x => x.VersionNumber).First().Title
            );

        var versionLookup = relevantPlans.ToDictionary(x => x.Id, x => x);

        var sharedPlanDtos = sharedPlans.Select(x =>
        {
            var title = string.Empty;
            if (x.PlanVersionId.HasValue && versionLookup.TryGetValue(x.PlanVersionId.Value, out var versionInfo))
            {
                title = versionInfo.Title;
            }
            else if (sharedPlanTitleLookup.TryGetValue(x.StudyPlanStableId, out var stableTitle))
            {
                title = stableTitle;
            }

            return new
            {
                studyPlanStableId = x.StudyPlanStableId,
                planVersionId = x.PlanVersionId,
                pinnedVersionNumber = x.PinnedVersionNumber,
                permission = x.Permission,
                autoEnroll = x.AutoEnroll,
                title
            };
        }).ToList();

        var cohorts = await _db.Cohorts.AsNoTracking()
            .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                c => c.StudyGroupStudyPlanId,
                sgsp => sgsp.Id,
                (c, sgsp) => new { Cohort = c, Adoption = sgsp })
            .Where(x => x.Adoption.StudyGroupId == gid)
            .Select(x => new SettingsCohortRow
                {
                    Id = x.Cohort.Id,
                    Title = x.Cohort.Title,
                    EnrollmentPolicy = x.Cohort.EnrollmentPolicy,
                    StudyPlanVersionId = x.Cohort.StudyPlanVersionId,
                    StudyPlanStableId = x.Adoption.StudyPlanStableId,
                    PinnedVersionNumber = x.Adoption.PinnedVersionNumber
                })
            .ToListAsync();

        var cohortDtos = cohorts.Select(c =>
        {
            var planStableId = c.StudyPlanStableId;
            var planTitle = string.Empty;
            if (versionLookup.TryGetValue(c.StudyPlanVersionId, out var versionInfo))
            {
                planTitle = versionInfo.Title;
                if (planStableId == Guid.Empty)
                {
                    planStableId = versionInfo.StableId;
                }
            }
            else if (planStableId != Guid.Empty && sharedPlanTitleLookup.TryGetValue(planStableId, out var stableTitle))
            {
                planTitle = stableTitle;
            }

            return new
            {
                id = c.Id,
                title = c.Title,
                planTitle,
                enrollMode = c.EnrollmentPolicy,
                pinnedVersionNumber = c.PinnedVersionNumber,
                planStableId,
                planVersionId = c.StudyPlanVersionId
            };
        }).ToList();

        List<object> myPlans = new();
        var isManager = role.Equals("Owner", StringComparison.OrdinalIgnoreCase)
            || role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            || role.Equals("Manager", StringComparison.OrdinalIgnoreCase);

        if (!isManager)
        {
            var myEnrollmentRows = await _db.UserGroups.AsNoTracking()
                .Where(x => x.UserId == userId && x.Status == "Active")
                .Join(_db.Cohorts.AsNoTracking().Where(c => c.Status == "Active"),
                    e => e.GroupId,
                    c => c.Id,
                    (e, c) => new { Membership = e, Cohort = c })
                .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                    x => x.Cohort.StudyGroupStudyPlanId,
                    sgsp => sgsp.Id,
                    (x, sgsp) => new { x.Membership, x.Cohort, Adoption = sgsp })
                .Where(x => x.Adoption.StudyGroupId == gid)
                .Select(x => new SettingsMyCohortPlanRow
                    {
                        CohortId = x.Cohort.Id,
                        JoinedAt = x.Membership.JoinedAt,
                        PlanVersionId = x.Cohort.StudyPlanVersionId,
                        PlanStableId = x.Adoption.StudyPlanStableId,
                        CohortTitle = x.Cohort.Title
                    })
                .ToListAsync();

            myPlans = myEnrollmentRows
                .GroupBy(x => x.PlanVersionId)
                .Select(g => g.OrderByDescending(x => x.JoinedAt).First())
                .Select(x =>
                {
                    var title = versionLookup.TryGetValue(x.PlanVersionId, out var versionInfo)
                        ? versionInfo.Title
                        : x.PlanVersionId.ToString();
                    return (object)new
                    {
                        planId = x.PlanStableId != Guid.Empty ? x.PlanStableId : (versionLookup.TryGetValue(x.PlanVersionId, out var info) ? info.StableId : x.PlanVersionId),
                        title,
                        cohortTitle = x.CohortTitle ?? string.Empty
                    };
                })
                .ToList();
        }

        return Ok(new
        {
            group,
            role,
            sharedPlans = sharedPlanDtos,
            cohorts = cohortDtos,
            myPlans
        });
    }

    [HttpGet("GetUserRoleInGroup/{groupId}")]
    [Authorize] // This endpoint requires user authorization
    public async Task<ActionResult<string>> GetUserRoleInGroup(string groupId)
    {
        // Retrieve the user's ID from the ClaimsPrincipal
        string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the user is authenticated
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("User is not authenticated.");
        }

        string userRole = await _studyGroupService.GetUserRoleInGroupAsync(groupId, userId);
        if (userRole == null)
        {
            return NotFound("User role not found.");
        }

        return Ok(userRole);
    }

    [HttpGet("GetStudyGroupMembers/{groupId}")]
    public async Task<ActionResult<IEnumerable<GroupMember>>> GetStudyGroupMembers(string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        var members = await _studyGroupService.GetStudyGroupMembers(groupId);
        if (members == null)
        {
            return NotFound("Study group not found.");
        }
        return Ok(members);
    }

    [HttpGet("Tags/{groupId}")]
    public async Task<ActionResult<IEnumerable<TagDTO>>> GetStudyGroupTags(string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        var tags = await _studyGroupService.GetGroupTagsAsync(groupId);
        return Ok(tags);
    }

    // [HttpGet("GetStudyGroupByUser/{userId}")]
    // public async Task<ActionResult<StudyGroup>> GetStudyGroupByUser(string userId)
    // {
    //     var group = await _studyGroupService.GetStudyGroupByUser(userId);
    //     if (group == null)
    //     {
    //         return NotFound("Study group not found.");
    //     }
    //     return Ok(group);
    // }

    [HttpGet("GetGroupManagers/{studyGroupId}")]
    public async Task<IActionResult> GetGroupManagers(string studyGroupId)
    {
        if (!Guid.TryParse(studyGroupId, out _))
        {
            return BadRequest("Invalid studyGroupId format. Expected GUID.");
        }
        var managerIds = await _studyGroupService.GetGroupManagersAsync(studyGroupId);
        if (managerIds == null || !managerIds.Any())
        {
            return NotFound("No managers found for this study group.");
        }
        return Ok(managerIds);
    }


    [HttpGet("GetStudyGroup")]
    [Authorize] // Ensure only authenticated users can access this endpoint
    public async Task<ActionResult<List<StudyGroup>>> GetStudyGroup([FromQuery] string? targetUserId = null)
    {
        // Retrieve the authenticated user's ID from the ClaimsPrincipal
        string currentUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the user is authenticated
        if (string.IsNullOrEmpty(currentUserId))
        {
            return Unauthorized("User is not authenticated.");
        }

        // If targetUserId is provided, use it; otherwise, use the authenticated user's ID
        string userIdToFetch = !string.IsNullOrEmpty(targetUserId) ? targetUserId : currentUserId;

        // Enforce privacy: if viewing another user's groups, check their visibility setting
        if (!string.IsNullOrEmpty(targetUserId) && targetUserId != currentUserId)
        {
            var targetPrivacy = await _db.Users.AsNoTracking()
                .Where(u => u.Id == targetUserId)
                .Select(u => new { u.ShowStudyGroupsPublicly })
                .FirstOrDefaultAsync();
            if (targetPrivacy != null && !targetPrivacy.ShowStudyGroupsPublicly)
            {
                return Ok(new List<StudyGroup>());
            }
        }

        // Fetch the study groups based on the provided or authenticated userId
        var groups = await _studyGroupService.GetStudyGroupByUser(userIdToFetch, currentUserId);

        // Return an empty list if no groups are found, instead of returning NotFound
        if (groups == null || !groups.Any())
        {
            return Ok(new List<StudyGroup>()); // Return an empty list if no study groups are found
        }

        return Ok(groups); // Return the found groups
    }

    [HttpPost("CreateStudyGroup")]
    [Authorize] // Ensure only authenticated users can access this endpoint
    public async Task<ActionResult> CreateStudyGroupAsync([FromBody] StudyGroupDTO studyGroupDTO)
    {
        // Retrieve the user's ID from the ClaimsPrincipal
        string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the user is authenticated
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("User is not authenticated.");
        }

        // Validate total tag count (<= 10), including new tag names
        var totalCount = (studyGroupDTO?.TagIds?.Distinct().Count() ?? 0) + (studyGroupDTO?.NewTagNames?.Select(s => s?.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Count() ?? 0);
        if (totalCount > 10)
        {
            return BadRequest("每个学习小组最多可添加 10 个标签。");
        }

        var moderation = await _contentModeration.ReviewTextAsync(
            new[] { studyGroupDTO?.Name, studyGroupDTO?.Description }.Concat(studyGroupDTO?.NewTagNames ?? Enumerable.Empty<string>()),
            HttpContext.RequestAborted);
        if (!moderation.Allowed)
        {
            return BadRequest(new
            {
                message = "内容未通过审核，请修改后再发布。",
                reason = moderation.Reason,
                blockedCategories = moderation.BlockedCategories
            });
        }

        var result = await _studyGroupService.CreateStudyGroupAsync(studyGroupDTO, userId);
        if (result)
        {
            return Ok("创建学习小组的申请已经成功提交审核！");
        }
        else
        {
            return BadRequest("创建学习小组的申请失败！请检查：小组名称是否已存在；标签是否重复、有效或数量不超过 10 个。");
        }
    }

    [HttpPost("ApproveStudyGroup")]
    [Authorize(Roles = "administrator")]
    public async Task<ActionResult> ApproveStudyGroupAsync([FromBody] string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        // Retrieve the admin's ID from the ClaimsPrincipal
        string adminUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the admin is authenticated
        if (string.IsNullOrEmpty(adminUserId))
        {
            return Unauthorized("Admin is not authenticated.");
        }

        var result = await _studyGroupService.ApproveStudyGroupAsync(groupId);
        if (result)
        {
            return Ok("Study group has been approved successfully.");
        }
        else
        {
            return BadRequest("Failed to approve the study group.");
        }
    }

    [HttpPost("RejectStudyGroup")]
    [Authorize(Roles = "administrator")]
    public async Task<ActionResult> RejectStudyGroupAsync([FromBody] string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        // Retrieve the admin's ID from the ClaimsPrincipal
        string adminUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the admin is authenticated
        if (string.IsNullOrEmpty(adminUserId))
        {
            return Unauthorized("Admin is not authenticated.");
        }

        var result = await _studyGroupService.RejectStudyGroupAsync(groupId);
        if (result)
        {
            return Ok("Study group has been rejected successfully.");
        }
        else
        {
            return BadRequest("Failed to reject the study group.");
        }
    }

    [HttpGet("ViewCreateStudyGroupRequests")]
    [Authorize(Roles = "administrator")]
    public async Task<ActionResult> ViewCreateStudyGroupRequests()
    {
        // Retrieve the admin's ID from the ClaimsPrincipal
        string adminUserId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the admin is authenticated
        if (string.IsNullOrEmpty(adminUserId))
        {
            return Unauthorized("Admin is not authenticated.");
        }

        var requests = await _studyGroupService.ViewCreateStudyGroupRequestsAsync();
        if (requests == null || !requests.Any())
        {
            return NotFound("No pending study group requests found.");
        }
        return Ok(requests);
    }

    [HttpDelete("DeleteStudyGroup/{groupId}")]
    [Authorize] // Ensure only authenticated users can access this endpoint
    public async Task<IActionResult> DeleteStudyGroup(string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        // Retrieve the user's ID from the ClaimsPrincipal
        string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the user is authenticated
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("User is not authenticated.");
        }

        var result = await _studyGroupService.DeleteStudyGroupAsync(groupId, userId);
        if (result)
        {
            return Ok("Study group deleted successfully.");
        }
        else
        {
            return BadRequest("Error deleting study group or permission denied.");
        }
    }

    [HttpPost("ApplyToJoin")]
    [Authorize] // Ensure only authenticated users can apply
    public async Task<IActionResult> ApplyToJoin([FromBody] ApplyToJoinRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Retrieve the user's ID from the ClaimsPrincipal
        string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the user is authenticated
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("User is not authenticated.");
        }

        if (string.IsNullOrEmpty(request.StudyGroupId))
        {
            return BadRequest("Study group ID is required.");
        }

        var result = await _studyGroupService.ApplyToJoin(userId, request.StudyGroupId);
        if (result)
        {
            return Ok("Application submitted successfully.");
        }
        else
        {
            return BadRequest("Failed to submit application. The group may not exist, or you may have already applied.");
        }
    }

    [HttpPost("LeaveStudyGroup")]
    public async Task<IActionResult> LeaveStudyGroup([FromBody] LeaveGroupRequest leaveGroupRequest)
    {
        try
        {
            var result = await _studyGroupService.LeaveStudyGroup(leaveGroupRequest.UserId, leaveGroupRequest.GroupId);

            if (result)
            {
                return Ok(new { message = "Successfully left the study group." });
            }
            else
            {
                return NotFound(new { message = "User was not a member of the study group." });
            }
        }
        catch (InvalidOperationException ex)
        {
            // Return a 400 Bad Request if the user is the manager and cannot leave
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpPost("DissolveStudyGroup")]
    public async Task<IActionResult> DissolveStudyGroup([FromBody] DissolveGroupRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.UserId))
            {
                return BadRequest("User ID is required.");
            }

            if (string.IsNullOrEmpty(request.GroupId))
            {
                return BadRequest("Group ID is required.");
            }

            var result = await _studyGroupService.DissolveStudyGroup(request.UserId, request.GroupId);

            return Ok(new { message = "Study group successfully dissolved." });
        }
        catch (UnauthorizedAccessException ex)
        {
            // Return a 403 Forbidden with a custom message
            return StatusCode(403, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Internal server error: {ex.Message}" });
        }
    }

    [HttpPost("UpdateApplicationStatus")]
    public async Task<IActionResult> UpdateApplicationStatus([FromBody] UpdateStatusRequest request)
    {
        if (string.IsNullOrEmpty(request.UserId) || string.IsNullOrEmpty(request.StudyGroupId) || request.Status == null)
        {
            return BadRequest("User ID, Study Group ID, and Status are required.");
        }

        var result = await _studyGroupService.UpdateApplicationStatusAsync(request.UserId, request.StudyGroupId, request.Status);
        if (result)
        {
            return Ok("Application status updated successfully.");
        }
        else
        {
            return BadRequest("Could not update the application status.");
        }
    }

    [HttpPost("JoinGroup/{groupId}")]
    public async Task<IActionResult> JoinGroup(string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        // Retrieve the user's ID from the ClaimsPrincipal
        string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // Ensure the user is authenticated
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("User is not authenticated.");
        }

        var result = await _studyGroupService.JoinGroupAsync(groupId, userId);
        if (result)
        {
            return Ok("Joined group successfully.");
        }
        else
        {
            return BadRequest("Error joining group or permission denied.");
        }
    }

    // Other endpoints like JoinGroup, PostUpdate, etc.
    [HttpGet("GetJoinRequests/{groupId}")]
    public async Task<ActionResult<IEnumerable<JoinRequest>>> GetJoinRequests(string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        var requests = await _studyGroupService.GetJoinRequests(groupId);
        if (requests == null)
        {
            return NotFound("Join requests not found.");
        }
        return Ok(requests);
    }

    // API to get the count of pending join requests
    [HttpGet("GetPendingJoinRequestsCount/{groupId}")]
    public async Task<IActionResult> GetPendingJoinRequestsCount(string groupId)
    {
        try
        {
            if (!Guid.TryParse(groupId, out _))
            {
                return BadRequest("Invalid groupId format. Expected GUID.");
            }
            // Call the service to get the count of pending join requests
            var pendingCount = await _studyGroupService.GetPendingJoinRequestsCount(groupId);

            return Ok(pendingCount);  // Return the count as the response
        }
        catch (Exception ex)
        {
            // Handle exceptions and return appropriate error response
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpGet("GetActivityLogs/{groupId}")]
    public async Task<ActionResult<IEnumerable<ActivityLog>>> GetActivityLogs(string groupId)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            return BadRequest("Invalid groupId format. Expected GUID.");
        }
        var logs = await _studyGroupService.GetActivityLogs(groupId);
        if (logs == null)
        {
            return NotFound("Activity logs not found.");
        }
        return Ok(logs);
    }
}
