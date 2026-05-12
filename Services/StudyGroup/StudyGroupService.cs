using Microsoft.AspNetCore.SignalR;
using Neo4j.Driver;
using Sciencetopia.Models;
using Sciencetopia.Services;
using Sciencetopia.Hubs;
using Newtonsoft.Json;
using Sciencetopia.Data;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Models.Enums;
using Microsoft.Extensions.Caching.Memory;
using GroupMemberDto = global::GroupMember;

    public class StudyGroupService
    {
    private readonly IDriver _neo4jDriver;
    private readonly UserService _userService;
    private readonly ApplicationDbContext _context;
    private readonly ITagResolutionService _tagResolution;
    private readonly ITagRepository _tagRepo;
    private readonly IHubContext<ChatHub> _hubContext;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan GroupDetailCacheTtl = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan GroupMemberCacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan GroupTagCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GroupPreviewCacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan GroupRoleCacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan JoinRequestCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ActivityLogCacheTtl = TimeSpan.FromSeconds(30);

    public StudyGroupService(IDriver neo4jDriver, UserService userService, IHubContext<ChatHub> hubContext, ApplicationDbContext context, ITagResolutionService tagResolution, ITagRepository tagRepo, IMemoryCache cache)
    {
        _neo4jDriver = neo4jDriver;
        _userService = userService;
        _hubContext = hubContext;
        _context = context;
        _tagResolution = tagResolution;
        _tagRepo = tagRepo;
        _cache = cache;
    }

    private void InvalidateGroupCache(Guid groupId)
    {
        if (groupId == Guid.Empty)
        {
            return;
        }

        _cache.Remove($"studygroup:detail:{groupId}");
        _cache.Remove($"studygroup:members:{groupId}");
        _cache.Remove($"studygroup:tags:{groupId}");
        _cache.Remove($"studygroup:preview:{groupId}:8");
        _cache.Remove($"studygroup:preview:{groupId}:8:group");
        _cache.Remove($"studygroup:joinrequests:{groupId}");
        _cache.Remove($"studygroup:joinrequests:count:{groupId}");
        _cache.Remove($"studygroup:activitylogs:{groupId}");
    }

    private void InvalidateGroupRequestCaches(Guid groupId)
    {
        if (groupId == Guid.Empty)
        {
            return;
        }

        _cache.Remove($"studygroup:joinrequests:{groupId}");
        _cache.Remove($"studygroup:joinrequests:count:{groupId}");
    }

    private static string GetRoleCacheKey(Guid groupId, string userId)
        => $"studygroup:role:{groupId}:{userId}";

    private static string ToRoleLabel(GroupRole role)
    {
        if (role == GroupRole.Owner) return "Owner";
        if (role >= GroupRole.Admin) return "Admin";
        return "Member";
    }

    private static string ToGraphRole(GroupRole role)
    {
        if (role == GroupRole.Owner) return "owner";
        if (role >= GroupRole.Admin) return "admin";
        return "member";
    }

    private static bool IsVisibleStudyGroupStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return true;
        return status.Equals("approved", StringComparison.OrdinalIgnoreCase)
            || status.Equals("active", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActiveMembershipStatus(string? status)
    {
        return string.IsNullOrWhiteSpace(status)
            || status.Equals("Active", StringComparison.OrdinalIgnoreCase);
    }

    // User-group permissions/roles source-of-truth: Groups.UserGroups
    private async Task<List<(Guid GroupId, string UserId, GroupRole Role, string? Status)>> GetMembershipRowsByGroupAsync(Guid groupId)
    {
        var rows = await _context.UserGroups.AsNoTracking()
            .Where(x => x.GroupId == groupId)
            .Select(x => new { x.GroupId, x.UserId, x.Role, x.Status })
            .ToListAsync();
        return rows.Select(x => (x.GroupId, x.UserId, x.Role, (string?)x.Status)).ToList();
    }

    private async Task<List<(Guid GroupId, string UserId, GroupRole Role, string? Status)>> GetMembershipRowsByUserAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new List<(Guid GroupId, string UserId, GroupRole Role, string? Status)>();
        }

        var rows = await _context.UserGroups.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.GroupId, x.UserId, x.Role, x.Status })
            .ToListAsync();
        return rows.Select(x => (x.GroupId, x.UserId, x.Role, (string?)x.Status)).ToList();
    }

    private async Task<(GroupRole Role, string? Status)?> GetMembershipRowAsync(Guid groupId, string userId)
    {
        if (groupId == Guid.Empty || string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var row = await _context.UserGroups.AsNoTracking()
            .Where(x => x.GroupId == groupId && x.UserId == userId)
            .Select(x => new { x.Role, x.Status })
            .FirstOrDefaultAsync();

        return row == null ? null : (row.Role, (string?)row.Status);
    }

    private async Task<Dictionary<Guid, List<GroupMemberDto>>> GetStudyGroupMembersByGroupsAsync(IEnumerable<Guid> groupIds)
    {
        var ids = groupIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

        var result = new Dictionary<Guid, List<GroupMemberDto>>();
        if (ids.Count == 0)
        {
            return result;
        }

        var missingGroupIds = new List<Guid>();
        foreach (var groupId in ids)
        {
            var cacheKey = $"studygroup:members:{groupId}";
            if (_cache.TryGetValue<List<GroupMemberDto>>(cacheKey, out var cachedMembers) && cachedMembers != null)
            {
                result[groupId] = cachedMembers.Select(m => new GroupMemberDto
                {
                    Id = m.Id,
                    UserName = m.UserName,
                    AvatarUrl = m.AvatarUrl,
                    Role = m.Role
                }).ToList();
            }
            else
            {
                missingGroupIds.Add(groupId);
            }
        }

        if (missingGroupIds.Count == 0)
        {
            return result;
        }

        var membershipRows = await _context.UserGroups.AsNoTracking()
            .Where(x => missingGroupIds.Contains(x.GroupId))
            .Select(x => new { x.GroupId, x.UserId, x.Role, x.Status })
            .ToListAsync();

        var activeRows = membershipRows
            .Where(x => IsActiveMembershipStatus(x.Status))
            .ToList();

        var displayInfoByUserId = await _userService.GetUserDisplayInfoByIdsAsync(activeRows.Select(x => x.UserId));
        var rowsByGroup = activeRows.GroupBy(x => x.GroupId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var groupId in missingGroupIds)
        {
            rowsByGroup.TryGetValue(groupId, out var rows);
            var members = new List<GroupMemberDto>(rows?.Count ?? 0);

            if (rows != null)
            {
                foreach (var row in rows)
                {
                    displayInfoByUserId.TryGetValue(row.UserId, out var displayInfo);
                    members.Add(new GroupMemberDto
                    {
                        Id = row.UserId,
                        UserName = displayInfo?.UserName ?? string.Empty,
                        AvatarUrl = displayInfo?.AvatarUrl ?? string.Empty,
                        Role = ToRoleLabel(row.Role)
                    });
                }
            }

            _cache.Set($"studygroup:members:{groupId}", members, GroupMemberCacheTtl);
            result[groupId] = members.Select(m => new GroupMemberDto
            {
                Id = m.Id,
                UserName = m.UserName,
                AvatarUrl = m.AvatarUrl,
                Role = m.Role
            }).ToList();
        }

        return result;
    }

    private async Task UpsertGraphMembershipAsync(string groupId, string userId, GroupRole role, DateTime? joinedAt = null)
    {
        var graphRole = ToGraphRole(role);
        var ts = (joinedAt ?? DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm:ss");
        using var session = _neo4jDriver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (s:StudyGroup {id:$groupId})
MATCH (u:User {id:$userId})
MERGE (u)-[r:MEMBER_OF]->(s)
SET r.role = $role,
    r.joinedAt = coalesce(r.joinedAt, $joinedAt)";
            await tx.RunAsync(cypher, new { groupId, userId, role = graphRole, joinedAt = ts });
        });
    }

    private async Task RemoveGraphMembershipAsync(string groupId, string userId)
    {
        using var session = _neo4jDriver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"MATCH (u:User {id:$userId})-[r:MEMBER_OF]->(sg:StudyGroup {id:$groupId}) DELETE r";
            await tx.RunAsync(cypher, new { userId, groupId });
        });
    }

    private async Task SyncGroupMetaToGraphAsync(string groupId, string? name = null, string? description = null, string? status = null)
    {
        using var session = _neo4jDriver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MERGE (s:StudyGroup {id:$groupId})
SET s.name = coalesce($name, s.name),
    s.description = coalesce($description, s.description),
    s.status = coalesce($status, s.status)";
            await tx.RunAsync(cypher, new { groupId, name, description, status });
        });
    }

    public async Task<bool> CreateStudyGroupAsync(StudyGroupDTO studyGroupDTO, string userId)
    {
        // Gather input tags: IDs and names
        var tagIds = (studyGroupDTO.TagIds ?? new List<Guid>())
            .Where(id => id != Guid.Empty)
            .ToList();
        var newTagNames = (studyGroupDTO.NewTagNames ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();

        // Check duplicate names (case-insensitive) in user input
        var nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var nm in newTagNames)
        {
            if (!nameSet.Add(nm))
            {
                return false; // duplicate names in input
            }
        }
        // Dedupe tagIds
        tagIds = tagIds.Distinct().ToList();

        var preUniqueCount = tagIds.Count + nameSet.Count;

        // Resolve or create new tag names, and ensure Uncategorized mapping
        if (newTagNames.Count > 0)
        {
            var (resolved, _) = await _tagResolution.ResolveOrCreateAsync(newTagNames, userId);
            tagIds.AddRange(resolved);
        }

        // Validate that referenced tag IDs exist
        if (tagIds.Count > 0)
        {
            var existingSet = await _context.Tags
                .Where(t => t.Id.HasValue && tagIds.Contains(t.Id.Value))
                .Select(t => t.Id!.Value)
                .ToListAsync();
            tagIds = tagIds.Intersect(existingSet).Distinct().ToList();
        }

        // Enforce not introducing duplicates across ids and resolved names
        tagIds = tagIds.Distinct().ToList();
        if (tagIds.Count < preUniqueCount)
        {
            // duplicates detected between provided ids and names mapping
            return false;
        }

        // Enforce maximum of 10 tags total
        if (tagIds.Count > 10) return false;
        // Check for duplicate name
        if (!string.IsNullOrEmpty(studyGroupDTO.Name))
        {
            var normalizedName = studyGroupDTO.Name.Trim().ToLower();

            bool exists = await _context.StudyGroups
                .AnyAsync(g => g.Name.Trim().ToLower() == normalizedName);
            if (exists) return false;
        }

        // Step 1: Save metadata in SQL (Group ancestor + StudyGroup flavor)
        var group = new GroupEntity
        {
            Kind = "StudyGroup",
            CreatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var entity = new StudyGroupEntity
        {
            Id = group.Id,
            Name = studyGroupDTO.Name ?? string.Empty,
            Description = studyGroupDTO.Description,
            CreatedAt = DateTime.UtcNow,
            Status = "pending_approval"
        };
        _context.Groups.Add(group);
        _context.StudyGroups.Add(entity);
        await _context.SaveChangesAsync();

        // Ensure SQL role consistency: creator becomes owner in SQL
        var existingRole = await _context.UserGroups
            .FirstOrDefaultAsync(r => r.GroupId == entity.Id && r.UserId == userId);
        if (existingRole == null)
        {
            _context.UserGroups.Add(new UserGroupEntity
            {
                GroupId = entity.Id,
                UserId = userId,
                Role = GroupRole.Owner
            });
            await _context.SaveChangesAsync();
        }

        // Step 2: Save relationship in Neo4j
        using var session = _neo4jDriver.AsyncSession();
        try
        {
            var result = await session.ExecuteWriteAsync(async tx =>
            {
                var query = @"
                MERGE (s:StudyGroup {id: $groupId})
                SET s.status = $status,
                    s.name = $name,
                    s.description = $description
                WITH s
                MATCH (u:User {id: $userId})
                MERGE (u)-[:MEMBER_OF {role: 'owner', joinedAt: $joinedAt}]->(s)
                WITH s
                FOREACH (tid IN $tagIds | MERGE (t:Tag {id: tid}) MERGE (t)-[:TAGGED_WITH]->(s))
                RETURN s.id AS groupId";

                var groupParams = new Dictionary<string, object>
                {
                    {"userId", userId},
                    {"groupId", entity.Id.ToString()},
                    {"joinedAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")},
                    {"status", entity.Status ?? "pending_approval"},
                    {"name", entity.Name},
                    {"description", entity.Description ?? string.Empty},
                    {"tagIds", tagIds.Select(x => x.ToString()).ToList()}
                };
                var cursor = await tx.RunAsync(query, groupParams);
                var record = await cursor.SingleAsync();
                return record["groupId"].As<string>() != null;
            });

            return result;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ApproveStudyGroupAsync(string groupId)
    {
        if (!Guid.TryParse(groupId, out var gid)) return false;
        var group = await _context.StudyGroups.FindAsync(gid);
        if (group == null) return false;

        group.Status = "approved";
        await _context.SaveChangesAsync();
        InvalidateGroupCache(gid);

        // Reflect status in Neo4j for consistency
        using (var session = _neo4jDriver.AsyncSession())
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                var q = "MERGE (s:StudyGroup {id:$id}) SET s.status=$status";
                await tx.RunAsync(q, new { id = groupId, status = group.Status });
            });
        }
        return true;
    }

    public async Task<bool> RejectStudyGroupAsync(string groupId)
    {
        if (!Guid.TryParse(groupId, out var gid)) return false;
        var group = await _context.StudyGroups.FindAsync(gid);
        if (group == null) return false;

        group.Status = "rejected";
        await _context.SaveChangesAsync();
        InvalidateGroupCache(gid);

        // Reflect status in Neo4j for consistency
        using (var session = _neo4jDriver.AsyncSession())
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                var q = "MERGE (s:StudyGroup {id:$id}) SET s.status=$status";
                await tx.RunAsync(q, new { id = groupId, status = group.Status });
            });
        }
        return true;
    }

    public async Task<List<StudyGroup>> GetAllStudyGroups()
    {
        var sqlGroups = await _context.StudyGroups
            .Where(g =>
                string.IsNullOrEmpty(g.Status)
                || g.Status == "approved"
                || g.Status == "Approved"
                || g.Status == "active"
                || g.Status == "Active")
            .ToListAsync();

        var enrichedGroups = new List<StudyGroup>();
        foreach (var entity in sqlGroups)
        {
            var groupId = entity.Id.ToString(); // Assuming Id is a Guid
            var members = await GetStudyGroupMembers(groupId);
            enrichedGroups.Add(new StudyGroup
            {
                Id = groupId,
                Name = entity.Name,
                Description = entity.Description,
                MemberIds = members,
                Status = entity.Status
            });
        }

        return enrichedGroups;
    }

    public async Task<List<StudyGroup>> GetStudyGroupsPagedAsync(int skip, int take)
    {
        var sqlGroups = await _context.StudyGroups
            .Where(g =>
                string.IsNullOrEmpty(g.Status)
                || g.Status == "approved"
                || g.Status == "Approved"
                || g.Status == "active"
                || g.Status == "Active")
            .OrderByDescending(g => g.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        var groupIds = sqlGroups.Select(x => x.Id).ToList();
        var membersByGroupId = await GetStudyGroupMembersByGroupsAsync(groupIds);
        var enrichedGroups = new List<StudyGroup>();
        foreach (var entity in sqlGroups)
        {
            var groupId = entity.Id.ToString();
            membersByGroupId.TryGetValue(entity.Id, out var members);
            enrichedGroups.Add(new StudyGroup
            {
                Id = groupId,
                Name = entity.Name,
                Description = entity.Description,
                MemberIds = members ?? new List<GroupMemberDto>(),
                Status = entity.Status,
                ImageUrl = entity.ImageUrl
            });
        }
        return enrichedGroups;
    }

    public async Task<List<TagDTO>> GetGroupTagsAsync(string groupId)
    {
        if (!Guid.TryParse(groupId, out var gid))
        {
            return new List<TagDTO>();
        }

        var cacheKey = $"studygroup:tags:{gid}";
        if (_cache.TryGetValue<List<TagDTO>>(cacheKey, out var cachedTags) && cachedTags != null)
        {
            return cachedTags.Select(x => new TagDTO { Id = x.Id, Name = x.Name }).ToList();
        }

        using var session = _neo4jDriver.AsyncSession();
        var tagIdStrings = await session.ExecuteReadAsync(async tx =>
        {
            var query = @"MATCH (t:Tag)-[:TAGGED_WITH]->(s:StudyGroup {id: $groupId}) RETURN t.id AS tagId";
            var cursor = await tx.RunAsync(query, new { groupId });
            var list = await cursor.ToListAsync(r => r["tagId"].As<string?>());
            return list.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        });

        var ids = new List<Guid>();
        foreach (var s in tagIdStrings)
        {
            if (Guid.TryParse(s, out var tagId)) ids.Add(tagId);
        }
        if (ids.Count == 0) return new List<TagDTO>();
        var nameMap = await _tagRepo.GetTagNamesAsync(ids);
        var tags = ids.Distinct().Select(id => new TagDTO { Id = id, Name = nameMap.TryGetValue(id, out var nm) ? nm : id.ToString() }).ToList();
        _cache.Set(cacheKey, tags, GroupTagCacheTtl);
        return tags;
    }

    public async Task<bool> UpdateGroupTagsAsync(string studyGroupId, List<Guid>? tagIds, List<string>? newTagNames, string userId)
    {
        var ids = (tagIds ?? new List<Guid>()).Where(x => x != Guid.Empty).Distinct().ToList();
        var newNames = (newTagNames ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();

        // Prevent duplicates by names
        var nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var nm in newNames)
        {
            if (!nameSet.Add(nm)) return false;
        }

        // Resolve or create new tag names and ensure valid ids
        if (newNames.Count > 0)
        {
            var (resolved, _) = await _tagResolution.ResolveOrCreateAsync(newNames, userId);
            ids.AddRange(resolved);
        }

        // Validate that referenced tag IDs exist in SQL
        if (ids.Count > 0)
        {
            var existingSet = await _context.Tags
                .Where(t => t.Id.HasValue && ids.Contains(t.Id.Value))
                .Select(t => t.Id!.Value)
                .ToListAsync();
            ids = ids.Intersect(existingSet).Distinct().ToList();
        }

        // Enforce maximum of 10 tags
        if (ids.Count > 10) return false;

        // Update Neo4j relationships atomically
        using var session = _neo4jDriver.AsyncSession();
        try
        {
            var ok = await session.ExecuteWriteAsync(async tx =>
            {
                var cypher = @"
                MATCH (s:StudyGroup {id: $groupId})
                OPTIONAL MATCH (t:Tag)-[r:TAGGED_WITH]->(s)
                DELETE r
                WITH s
                FOREACH (tid IN $tagIds | MERGE (t:Tag {id: tid}) MERGE (t)-[:TAGGED_WITH]->(s))
                RETURN s.id AS id";
                var p = new
                {
                    groupId = studyGroupId,
                    tagIds = ids.Select(x => x.ToString()).ToList()
                };
                var cursor = await tx.RunAsync(cypher, p);
                var record = await cursor.SingleAsync();
                return record["id"].As<string>() != null;
            });
            InvalidateGroupCache(Guid.Parse(studyGroupId));
            return ok;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<GroupMemberDto>> GetStudyGroupMembers(string groupId)
    {
        if (!Guid.TryParse(groupId, out var gid))
        {
            return new List<GroupMemberDto>();
        }

        var cacheKey = $"studygroup:members:{gid}";
        if (_cache.TryGetValue<List<GroupMemberDto>>(cacheKey, out var cachedMembers) && cachedMembers != null)
        {
            return cachedMembers.Select(m => new GroupMemberDto
            {
                Id = m.Id,
                UserName = m.UserName,
                AvatarUrl = m.AvatarUrl,
                Role = m.Role
            }).ToList();
        }

        var rows = (await GetMembershipRowsByGroupAsync(gid))
            .Where(x => IsActiveMembershipStatus(x.Status))
            .Select(x => new { x.UserId, x.Role })
            .ToList();

        var displayInfoByUserId = await _userService.GetUserDisplayInfoByIdsAsync(rows.Select(x => x.UserId));
        var members = new List<GroupMemberDto>(rows.Count);
        foreach (var row in rows)
        {
            displayInfoByUserId.TryGetValue(row.UserId, out var displayInfo);

            members.Add(new GroupMemberDto
            {
                Id = row.UserId,
                UserName = displayInfo?.UserName ?? string.Empty,
                AvatarUrl = displayInfo?.AvatarUrl ?? string.Empty,
                Role = ToRoleLabel(row.Role)
            });
        }

        _cache.Set(cacheKey, members, GroupMemberCacheTtl);
        return members;
    }

    public async Task<List<GroupMemberDto>> GetStudyGroupMemberPreviewAsync(string groupId, int take = 8)
    {
        if (!Guid.TryParse(groupId, out var gid))
        {
            return new List<GroupMemberDto>();
        }

        take = Math.Max(1, take);
        var cacheKey = $"studygroup:preview:{gid}:{take}";
        if (_cache.TryGetValue<List<GroupMemberDto>>(cacheKey, out var cachedMembers) && cachedMembers != null)
        {
            return cachedMembers.Select(m => new GroupMemberDto
            {
                Id = m.Id,
                UserName = m.UserName,
                AvatarUrl = m.AvatarUrl,
                Role = m.Role
            }).ToList();
        }

        var rows = await _context.UserGroups.AsNoTracking()
            .Where(x => x.GroupId == gid && (string.IsNullOrEmpty(x.Status) || x.Status == "Active" || x.Status == "active"))
            .OrderByDescending(x => x.Role)
            .ThenBy(x => x.JoinedAt)
            .Select(x => new { x.UserId, x.Role })
            .Take(take)
            .ToListAsync();

        var displayInfoByUserId = await _userService.GetUserDisplayInfoByIdsAsync(rows.Select(x => x.UserId));
        var members = rows.Select(row =>
        {
            displayInfoByUserId.TryGetValue(row.UserId, out var displayInfo);
            return new GroupMemberDto
            {
                Id = row.UserId,
                UserName = displayInfo?.UserName ?? string.Empty,
                AvatarUrl = displayInfo?.AvatarUrl ?? string.Empty,
                Role = ToRoleLabel(row.Role)
            };
        }).ToList();

        _cache.Set(cacheKey, members, GroupPreviewCacheTtl);
        return members;
    }

    public async Task<StudyGroup?> GetStudyGroupPreviewByIdAsync(string groupId, int memberLimit = 8)
    {
        if (!Guid.TryParse(groupId, out var gid)) return null;

        var cacheKey = $"studygroup:preview:{gid}:{memberLimit}:group";
        if (_cache.TryGetValue<StudyGroup>(cacheKey, out var cachedGroup) && cachedGroup != null)
        {
            return new StudyGroup
            {
                Id = cachedGroup.Id,
                Name = cachedGroup.Name,
                Description = cachedGroup.Description,
                MemberIds = cachedGroup.MemberIds?.Select(m => new GroupMemberDto
                {
                    Id = m.Id,
                    UserName = m.UserName,
                    AvatarUrl = m.AvatarUrl,
                    Role = m.Role
                }).ToList(),
                ImageUrl = cachedGroup.ImageUrl,
                Status = cachedGroup.Status,
                Role = cachedGroup.Role
            };
        }

        var entity = await _context.StudyGroups.AsNoTracking()
            .Where(x => x.Id == gid)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Description,
                x.ImageUrl,
                x.Status
            })
            .FirstOrDefaultAsync();

        if (entity == null) return null;

        var members = await GetStudyGroupMemberPreviewAsync(groupId, memberLimit);
        var result = new StudyGroup
        {
            Id = entity.Id.ToString(),
            Name = entity.Name,
            Description = entity.Description,
            MemberIds = members,
            ImageUrl = entity.ImageUrl,
            Status = entity.Status
        };

        _cache.Set(cacheKey, result, GroupPreviewCacheTtl);
        return result;
    }

    public async Task<List<string>> GetGroupManagersAsync(string studyGroupId)
    {
        if (!Guid.TryParse(studyGroupId, out var gid))
        {
            return new List<string>();
        }

        return (await GetMembershipRowsByGroupAsync(gid))
            .Where(x => IsActiveMembershipStatus(x.Status) && x.Role >= GroupRole.Admin)
            .Select(x => x.UserId)
            .Distinct()
            .ToList();
    }

    // Rest of the code...
    public async Task<bool> DeleteStudyGroupAsync(string groupId, string userId)
    {
        // Fetch the study group by groupId
        var studyGroup = await GetStudyGroupByIdAsync(groupId);
        if (studyGroup == null)
        {
            // Study group does not exist
            return false;
        }

        // Check if the user is the manager of the study group
        if (!await IsUserManagerAsync(groupId, userId))
        {
            // User is not authorized to delete the study group
            throw new UnauthorizedAccessException("Only the manager can delete the study group.");
        }

        // Logic to delete the study group
        await DeleteStudyGroupFromDatabaseAsync(groupId);
        InvalidateGroupCache(Guid.Parse(groupId));

        return true; // Return true if deletion is successful
    }

    public async Task<StudyGroup> GetStudyGroupByIdAsync(string groupId)
    {
        if (!Guid.TryParse(groupId, out var gid)) return null;
        var cacheKey = $"studygroup:detail:{gid}";
        if (_cache.TryGetValue<StudyGroup>(cacheKey, out var cachedGroup) && cachedGroup != null)
        {
            return new StudyGroup
            {
                Id = cachedGroup.Id,
                Name = cachedGroup.Name,
                Description = cachedGroup.Description,
                MemberIds = cachedGroup.MemberIds?.Select(m => new GroupMemberDto
                {
                    Id = m.Id,
                    UserName = m.UserName,
                    AvatarUrl = m.AvatarUrl,
                    Role = m.Role
                }).ToList(),
                ImageUrl = cachedGroup.ImageUrl,
                Status = cachedGroup.Status,
                Role = cachedGroup.Role
            };
        }
        var entity = await _context.StudyGroups.FindAsync(gid);
        if (entity == null) return null;

        var members = await GetStudyGroupMembers(groupId);
        var result = new StudyGroup
        {
            Id = entity.Id.ToString(),
            Name = entity.Name,
            Description = entity.Description,
            MemberIds = members,
            ImageUrl = entity.ImageUrl,
            Status = entity.Status
        };
        _cache.Set(cacheKey, result, GroupDetailCacheTtl);
        return result;
    }

    public async Task<List<StudyGroup>> SearchStudyGroups(string query, int skip, int take)
    {
        var sqlGroups = await _context.StudyGroups
            .Where(g =>
                EF.Functions.Like(g.Name.ToLower(), $"%{query.ToLower()}%")
                && (string.IsNullOrEmpty(g.Status)
                    || g.Status == "approved"
                    || g.Status == "Approved"
                    || g.Status == "active"
                    || g.Status == "Active"))
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        var enrichedGroups = new List<StudyGroup>();
        foreach (var entity in sqlGroups)
        {
            var groupId = entity.Id.ToString(); // Assuming Id is a Guid
            var members = await GetStudyGroupMembers(groupId);
            enrichedGroups.Add(new StudyGroup
            {
                Id = groupId,
                Name = entity.Name,
                Description = entity.Description,
                MemberIds = members,
                Status = entity.Status
            });
        }

        return enrichedGroups;
    }

    private async Task DeleteStudyGroupFromDatabaseAsync(string groupId)
    {
        // Delete the study group from SQL database
        if (!Guid.TryParse(groupId, out var gid)) return;
        var studyGroup = await _context.StudyGroups.FindAsync(gid);
        var ancestorGroup = await _context.Groups.FindAsync(gid);
        if (studyGroup != null) _context.StudyGroups.Remove(studyGroup);
        if (ancestorGroup != null) _context.Groups.Remove(ancestorGroup);
        if (studyGroup != null || ancestorGroup != null)
        {
            await _context.SaveChangesAsync();
        }

        // Delete the study group from Neo4j database
        using (var session = _neo4jDriver.AsyncSession())
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                var query = @"MATCH (s:StudyGroup {id: $groupId}) DETACH DELETE s";
                var parameters = new Dictionary<string, object>
                {
                {"groupId", groupId}
                };

                await tx.RunAsync(query, parameters);
            });
        }
    }

    public async Task<bool> ApplyToJoin(string userId, string studyGroupId)
    {
        // Guard by approval status
        if (!await IsGroupApprovedAsync(studyGroupId))
            return false;
        var session = _neo4jDriver.AsyncSession();
        try
        {
            var result = await session.ExecuteWriteAsync(async tx =>
            {
                var cursor = await tx.RunAsync(
                    "MERGE (u:User {id: $userId}) " +
                    "MERGE (sg:StudyGroup {id: $studyGroupId}) " +
                    "MERGE (u)-[r:APPLIED_TO {status: 'Pending', appliedOn: $appliedOn}]->(sg) " +
                    "RETURN r",
                    new { userId, studyGroupId, appliedOn = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss") });

                return await cursor.SingleAsync() != null;
            });

            if (result)
            {
                if (Guid.TryParse(studyGroupId, out var gid))
                {
                    InvalidateGroupRequestCaches(gid);
                }
                // Notify all the managers of the study group about the application
                await NotifyStudyGroupManagers(studyGroupId, userId);
            }

            return result;
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    private async Task NotifyStudyGroupManagers(string studyGroupId, string userId)
    {
        if (!Guid.TryParse(studyGroupId, out var gid))
        {
            return;
        }

        var managerIds = await _context.UserGroups.AsNoTracking()
            .Where(x => x.GroupId == gid && x.Role >= GroupRole.Admin)
            .Select(x => x.UserId)
            .ToListAsync();
        if (managerIds.Count == 0) return;

        var studyGroupName = await _context.StudyGroups.AsNoTracking()
            .Where(x => x.Id == gid)
            .Select(x => x.Name)
            .FirstOrDefaultAsync() ?? studyGroupId;

        var notificationContent = $"User {userId} has applied to join your study group {studyGroupName}.";
        var notificationType = "StudyGroupApplication";
        var notificationUrl = $"/studygroup/{studyGroupId}";
        var createdAt = DateTime.UtcNow;

        var notifications = managerIds.Select(managerId => new Notification
        {
            Content = notificationContent,
            CreatedAt = createdAt,
            IsRead = false,
            UserId = managerId,
            Type = notificationType,
            Data = notificationUrl
        }).ToList();

        _context.Notifications.AddRange(notifications);
        await _context.SaveChangesAsync();

        foreach (var notification in notifications)
        {
            await _hubContext.Clients.User(notification.UserId).SendAsync("ReceiveNotification", new
            {
                notification.Id,
                notification.Content,
                notification.CreatedAt,
                notification.IsRead,
                notification.Type,
                notification.Data
            });

            await _hubContext.Clients.User(notification.UserId).SendAsync("UpdateNotifications");
        }
    }

    public async Task<bool> UpdateApplicationStatusAsync(string userId, string studyGroupId, string status)
    {
        // Only operate on approved groups
        if (!await IsGroupApprovedAsync(studyGroupId))
            return false;
        var session = _neo4jDriver.AsyncSession();
        try
        {
            var result = await session.ExecuteWriteAsync(async tx =>
            {
                var cursor = await tx.RunAsync(
                    "MATCH (u:User {id: $userId})-[r:APPLIED_TO]->(sg:StudyGroup {id: $studyGroupId}) " +
                    "SET r.status = $status " +
                    "RETURN r",
                    new { userId, studyGroupId, status });
                return await cursor.SingleAsync() != null;
            });

            // If the application was approved and successfully updated, add the user to the group
            if (status == "Approved" && result)
            {
                if (Guid.TryParse(studyGroupId, out var gid))
                {
                    InvalidateGroupRequestCaches(gid);
                }
                return await JoinGroupAsync(studyGroupId, userId);
            }

            if (result && Guid.TryParse(studyGroupId, out var groupGuid))
            {
                InvalidateGroupRequestCaches(groupGuid);
            }

            return result;
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    internal async Task<bool> JoinGroupAsync(string groupId, string userId)
    {
        if (!await IsGroupApprovedAsync(groupId))
        {
            return false;
        }
        if (!Guid.TryParse(groupId, out var gid))
        {
            return false;
        }

        var membership = await _context.UserGroups.FirstOrDefaultAsync(r => r.GroupId == gid && r.UserId == userId);
        if (membership == null)
        {
            membership = new UserGroupEntity { GroupId = gid, UserId = userId, Role = GroupRole.Member };
            _context.UserGroups.Add(membership);
            await _context.SaveChangesAsync();
        }
        else if (!string.Equals(membership.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            membership.Status = "Active";
            membership.LeftAt = null;
            await _context.SaveChangesAsync();
        }

        InvalidateGroupCache(gid);
        _cache.Remove(GetRoleCacheKey(gid, userId));
        await UpsertGraphMembershipAsync(groupId, userId, membership.Role);
        return true;
    }

    public async Task<bool> LeaveStudyGroup(string userId, string groupId)
    {
        // SQL authority check
        if (await IsUserManagerAsync(groupId, userId))
            throw new InvalidOperationException("Managers cannot leave their own study group. You must dissolve the group.");

        await RemoveGraphMembershipAsync(groupId, userId);

        // Remove SQL role
        if (Guid.TryParse(groupId, out var gid))
        {
            var role = await _context.UserGroups.FirstOrDefaultAsync(r => r.GroupId == gid && r.UserId == userId);
            if (role != null)
            {
                _context.UserGroups.Remove(role);
                await _context.SaveChangesAsync();
            }
            InvalidateGroupCache(gid);
            _cache.Remove(GetRoleCacheKey(gid, userId));
        }

        return true;
    }

    public async Task<bool> DissolveStudyGroup(string userId, string groupId)
    {
        // SQL authority check
        if (!await IsUserManagerAsync(groupId, userId))
            throw new UnauthorizedAccessException("Only the manager can dissolve the study group.");

        var studyGroup = await GetStudyGroupByIdAsync(groupId);
        if (studyGroup == null)
            throw new InvalidOperationException("Study group does not exist.");

        await DeleteStudyGroupFromDatabaseAsync(groupId);
        return true;
    }

    internal async Task<List<StudyGroup>> GetStudyGroupByUser(string userId, string currentUserId)
    {
        var memberships = (await GetMembershipRowsByUserAsync(userId))
            .Where(x => x.UserId == userId && IsActiveMembershipStatus(x.Status))
            .GroupBy(x => x.GroupId)
            .Select(g =>
            {
                var best = g.OrderByDescending(x => x.Role).First();
                return (GroupId: g.Key, UserId: best.UserId, Role: best.Role, Status: best.Status);
            })
            .ToList();

        if (memberships.Count == 0)
        {
            return new List<StudyGroup>();
        }

        var groupIds = memberships.Select(x => x.GroupId).Distinct().ToList();
        var groups = await _context.StudyGroups.AsNoTracking()
            .Where(x => groupIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x);

        var studyGroups = new List<StudyGroup>();
        foreach (var membership in memberships)
        {
            if (!groups.TryGetValue(membership.GroupId, out var groupEntity)) continue;
            var studyGroupId = groupEntity.Id.ToString();
            var members = await GetStudyGroupMembers(studyGroupId);

            studyGroups.Add(new StudyGroup
            {
                Id = studyGroupId,
                Name = groupEntity.Name,
                Description = groupEntity.Description,
                MemberIds = members,
                Status = groupEntity.Status,
                Role = ToRoleLabel(membership.Role)
            });
        }

        return studyGroups;
    }

    public async Task<List<StudyGroup>> ViewCreateStudyGroupRequestsAsync()
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var query = @"
                MATCH (s:StudyGroup {status: 'pending_approval'})
                RETURN s";

                var cursor = await tx.RunAsync(query);
                return await cursor.ToListAsync();
            });

            var studyGroups = new List<StudyGroup>();
            foreach (var record in result)
            {
                var studyGroupNode = record["s"].As<INode>();

                var studyGroup = new StudyGroup
                {
                    Id = studyGroupNode.Properties["id"].As<string>(),
                    Name = studyGroupNode.Properties["name"].As<string>(),
                    Description = studyGroupNode.Properties["description"].As<string>(),
                    Status = studyGroupNode.Properties["status"].As<string>()
                };
                studyGroups.Add(studyGroup);
            }

            return studyGroups;
        }
    }

    public async Task<bool> InviteMemberAsync(string studyGroupId, string memberId)
    {
        if (!await IsGroupApprovedAsync(studyGroupId))
        {
            return false;
        }
        if (!Guid.TryParse(studyGroupId, out var gid))
        {
            return false;
        }

        var membership = await _context.UserGroups.FirstOrDefaultAsync(r => r.GroupId == gid && r.UserId == memberId);
        if (membership == null)
        {
            membership = new UserGroupEntity { GroupId = gid, UserId = memberId, Role = GroupRole.Member, Status = "Active" };
            _context.UserGroups.Add(membership);
            await _context.SaveChangesAsync();
        }
        else if (!string.Equals(membership.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            membership.Status = "Active";
            membership.LeftAt = null;
            await _context.SaveChangesAsync();
        }

        InvalidateGroupCache(gid);
        _cache.Remove(GetRoleCacheKey(gid, memberId));
        await UpsertGraphMembershipAsync(studyGroupId, memberId, membership.Role);
        return true;
    }

    public async Task<bool> ApproveJoinRequestAsync(string studyGroupId, string memberId)
    {
        if (!await IsGroupApprovedAsync(studyGroupId))
        {
            return false;
        }

        using var session = _neo4jDriver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var query = @"
MATCH (u:User {id: $memberId})-[r:APPLIED_TO]->(s:StudyGroup {id: $studyGroupId})
DELETE r";
            await tx.RunAsync(query, new { studyGroupId, memberId });
        });

        if (Guid.TryParse(studyGroupId, out var gid))
        {
            InvalidateGroupRequestCaches(gid);
        }

        return await JoinGroupAsync(studyGroupId, memberId);
    }

    public async Task<bool> DeleteMemberAsync(string studyGroupId, string memberId)
    {
        if (!Guid.TryParse(studyGroupId, out var gid))
        {
            return false;
        }

        var existing = await _context.UserGroups.FirstOrDefaultAsync(r => r.GroupId == gid && r.UserId == memberId);
        if (existing != null)
        {
            _context.UserGroups.Remove(existing);
            await _context.SaveChangesAsync();
        }

        InvalidateGroupCache(gid);
        _cache.Remove(GetRoleCacheKey(gid, memberId));
        await RemoveGraphMembershipAsync(studyGroupId, memberId);
        return existing != null;
    }

    public async Task<bool> TransferManagerRoleAsync(string studyGroupId, string newManagerId)
    {
        if (!Guid.TryParse(studyGroupId, out var gid))
        {
            return false;
        }

        var existing = await _context.UserGroups.FirstOrDefaultAsync(r => r.GroupId == gid && r.UserId == newManagerId);
        if (existing == null) return false;

        if (existing.Role < GroupRole.Admin)
        {
            existing.Role = GroupRole.Admin;
            await _context.SaveChangesAsync();
        }

        InvalidateGroupCache(gid);
        _cache.Remove(GetRoleCacheKey(gid, newManagerId));
        await UpsertGraphMembershipAsync(studyGroupId, newManagerId, GroupRole.Admin);
        return true;
    }

    private async Task<bool> IsGroupApprovedAsync(string groupId)
    {
        if (!Guid.TryParse(groupId, out var gid)) return false;
        var status = await _context.StudyGroups.AsNoTracking()
            .Where(g => g.Id == gid)
            .Select(g => g.Status)
            .FirstOrDefaultAsync();
        return IsVisibleStudyGroupStatus(status);
    }

    public async Task<bool> RenameGroupAsync(string studyGroupId, string newName)
    {
        if (!Guid.TryParse(studyGroupId, out var gid))
        {
            return false;
        }

        var group = await _context.StudyGroups.FirstOrDefaultAsync(x => x.Id == gid);
        if (group == null) return false;

        group.Name = newName;
        var ancestor = await _context.Groups.FirstOrDefaultAsync(x => x.Id == gid);
        if (ancestor != null) ancestor.UpdatedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();
        InvalidateGroupCache(gid);
        await SyncGroupMetaToGraphAsync(studyGroupId, name: newName);
        return true;
    }

    public async Task<bool> EditDescriptionAsync(string studyGroupId, string newDescription)
    {
        if (!Guid.TryParse(studyGroupId, out var gid)) return false;
        var group = await _context.StudyGroups.FindAsync(gid);
        if (group == null) return false;

        group.Description = newDescription;
        var ancestor = await _context.Groups.FirstOrDefaultAsync(x => x.Id == gid);
        if (ancestor != null) ancestor.UpdatedAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();
        InvalidateGroupCache(gid);
        await SyncGroupMetaToGraphAsync(studyGroupId, description: newDescription);
        return true;
    }

    public async Task<bool> EditProfilePictureAsync(string studyGroupId, string newProfilePictureUrl)
    {
        var group = await _context.StudyGroups.FindAsync(Guid.Parse(studyGroupId));
        if (group == null) return false;

        group.ImageUrl = newProfilePictureUrl;
        await _context.SaveChangesAsync();
        InvalidateGroupCache(Guid.Parse(studyGroupId));
        return true;
    }

    public async Task<bool> IsUserManagerAsync(string studyGroupId, string userId)
    {
        if (!Guid.TryParse(studyGroupId, out var gid)) return false;
        var row = await GetMembershipRowAsync(gid, userId);
        return row.HasValue
            && row.Value.Role >= GroupRole.Admin
            && IsActiveMembershipStatus(row.Value.Status);
    }

    public async Task<bool> IsUserMemberAsync(string studyGroupId, string userId)
    {
        if (!Guid.TryParse(studyGroupId, out var gid)) return false;
        var row = await GetMembershipRowAsync(gid, userId);
        return row.HasValue && IsActiveMembershipStatus(row.Value.Status);
    }

    public async Task<string> GetUserRoleInGroupAsync(string groupId, string userId)
    {
        if (!Guid.TryParse(groupId, out var gid))
        {
            return null;
        }

        var cacheKey = GetRoleCacheKey(gid, userId);
        if (_cache.TryGetValue<string>(cacheKey, out var cachedRole))
        {
            return cachedRole;
        }

        var row = await GetMembershipRowAsync(gid, userId);
        var resolvedRole = row.HasValue && IsActiveMembershipStatus(row.Value.Status)
            ? ToRoleLabel(row.Value.Role)
            : null;
        if (resolvedRole != null)
        {
            _cache.Set(cacheKey, resolvedRole, GroupRoleCacheTtl);
        }

        return resolvedRole;
    }

    public async Task<IEnumerable<JoinRequest>> GetJoinRequests(string groupId)
    {
        if (!Guid.TryParse(groupId, out var gid))
        {
            return Enumerable.Empty<JoinRequest>();
        }

        var cacheKey = $"studygroup:joinrequests:{gid}";
        if (_cache.TryGetValue<List<JoinRequest>>(cacheKey, out var cachedRequests) && cachedRequests != null)
        {
            return cachedRequests.Select(x => new JoinRequest
            {
                UserId = x.UserId,
                Name = x.Name,
                AppliedOn = x.AppliedOn,
                AvatarUrl = x.AvatarUrl
            }).ToList();
        }

        if (!await IsGroupApprovedAsync(groupId))
            return Enumerable.Empty<JoinRequest>();
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var query = @"
                MATCH (u:User)-[r:APPLIED_TO]->(s:StudyGroup {id: $groupId})
                WHERE coalesce(r.status, 'Pending') = 'Pending'
                RETURN u.id AS userId, r.appliedOn AS appliedOn
                ORDER BY r.appliedOn DESC";
                var parameters = new { groupId };
                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.ToListAsync(record => new
                {
                    UserId = record["userId"].As<string>(),
                    AppliedOn = record["appliedOn"].As<string>()
                });
            });

            var joinRequests = new List<JoinRequest>();
            var displayInfoByUserId = await _userService.GetUserDisplayInfoByIdsAsync(result.Select(record => record.UserId));

            foreach (var record in result)
            {
                displayInfoByUserId.TryGetValue(record.UserId, out var displayInfo);

                var joinRequest = new JoinRequest
                {
                    UserId = record.UserId,
                    Name = displayInfo?.UserName ?? string.Empty,
                    AvatarUrl = displayInfo?.AvatarUrl ?? string.Empty,
                    AppliedOn = record.AppliedOn
                };

                joinRequests.Add(joinRequest);
            }

            _cache.Set(cacheKey, joinRequests, JoinRequestCacheTtl);
            _cache.Set($"studygroup:joinrequests:count:{gid}", joinRequests.Count, JoinRequestCacheTtl);

            return joinRequests;
        }
    }

    public async Task<int> GetPendingJoinRequestsCount(string groupId)
    {
        if (!Guid.TryParse(groupId, out var gid))
        {
            return 0;
        }

        var cacheKey = $"studygroup:joinrequests:count:{gid}";
        if (_cache.TryGetValue<int>(cacheKey, out var cachedCount))
        {
            return cachedCount;
        }

        if (!await IsGroupApprovedAsync(groupId))
            return 0;
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var query = @"
            MATCH (u:User)-[r:APPLIED_TO {status: 'Pending'}]->(s:StudyGroup {id: $groupId})
            RETURN COUNT(r) AS pendingCount";

                var parameters = new { groupId };
                var cursor = await tx.RunAsync(query, parameters);

                var record = await cursor.SingleAsync();
                return record["pendingCount"].As<int>();  // Return the count of pending join requests
            });

            _cache.Set(cacheKey, result, JoinRequestCacheTtl);
            return result;
        }
    }

    public async Task<IEnumerable<ActivityLog>> GetActivityLogs(string groupId)
    {
        if (!Guid.TryParse(groupId, out var gid))
        {
            return Enumerable.Empty<ActivityLog>();
        }

        var cacheKey = $"studygroup:activitylogs:{gid}";
        if (_cache.TryGetValue<List<ActivityLog>>(cacheKey, out var cachedLogs) && cachedLogs != null)
        {
            return cachedLogs.Select(x => new ActivityLog
            {
                Id = x.Id,
                Message = x.Message,
                Date = x.Date
            }).ToList();
        }

        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var query = @"
                MATCH (s:StudyGroup {id: $groupId})-[:HAS_LOG]->(l:ActivityLog)
                RETURN l.message AS message, l.date AS date
                ORDER BY l.date DESC
                LIMIT 100";
                var parameters = new { groupId };
                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.ToListAsync(record => new ActivityLog
                {
                    Message = record["message"].As<string>(),
                    Date = record["date"].As<string>()
                });
            });

            _cache.Set(cacheKey, result.ToList(), ActivityLogCacheTtl);
            return result;
        }
    }
}
