using Microsoft.AspNetCore.SignalR;
using Neo4j.Driver;
using Sciencetopia.Models;
using Sciencetopia.Services;
using Sciencetopia.Hubs;
using Newtonsoft.Json;
using Sciencetopia.Data;
using Microsoft.EntityFrameworkCore;

public class StudyGroupService
{
    private readonly IDriver _neo4jDriver;
    private readonly UserService _userService;
    private readonly ApplicationDbContext _context;
    private readonly IHubContext<ChatHub> _hubContext;

    public StudyGroupService(IDriver neo4jDriver, UserService userService, IHubContext<ChatHub> hubContext, ApplicationDbContext context)
    {
        _neo4jDriver = neo4jDriver;
        _userService = userService;
        _hubContext = hubContext;
        _context = context;
    }

    public async Task<bool> CreateStudyGroupAsync(StudyGroupDTO studyGroupDTO, string userId)
    {
        // Check for duplicate name
        if (!string.IsNullOrEmpty(studyGroupDTO.Name))
        {
            var normalizedName = studyGroupDTO.Name.Trim().ToLower();

            bool exists = await _context.StudyGroups
                .AnyAsync(g => g.Name.Trim().ToLower() == normalizedName);
            if (exists) return false;
        }

        // Step 1: Save metadata in SQL
        var entity = new StudyGroupEntity
        {
            Name = studyGroupDTO.Name ?? string.Empty,
            Description = studyGroupDTO.Description,
            CreatedAt = DateTime.UtcNow,
            Status = "pending_approval"
        };
        _context.StudyGroups.Add(entity);
        await _context.SaveChangesAsync();

        // Step 2: Save relationship in Neo4j
        using var session = _neo4jDriver.AsyncSession();
        try
        {
            var result = await session.ExecuteWriteAsync(async tx =>
            {
                var query = @"
                MERGE (s:StudyGroup {id: $groupId})
                WITH s
                MATCH (u:User {id: $userId})
                CREATE (u)-[:MEMBER_OF {role: 'manager', joinedAt: $joinedAt}]->(s)
                RETURN s.id AS groupId";

                var groupParams = new Dictionary<string, object>
                {
                    {"userId", userId},
                    {"groupId", entity.Id.ToString()},
                    {"joinedAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")}
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
        var group = await _context.StudyGroups.FindAsync(groupId);
        if (group == null) return false;

        group.Status = "approved";
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RejectStudyGroupAsync(string groupId)
    {
        var group = await _context.StudyGroups.FindAsync(groupId);
        if (group == null) return false;

        group.Status = "rejected";
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<StudyGroup>> GetAllStudyGroups()
    {
        var sqlGroups = await _context.StudyGroups
            .Where(g => g.Status == "approved")
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

    public async Task<List<GroupMember>> GetStudyGroupMembers(string groupId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var query = @"
                MATCH (u:User)-[r:MEMBER_OF]->(s:StudyGroup {id: $groupId})
                RETURN u.id AS userId, r.role AS role";
                var parameters = new Dictionary<string, object>
                {
                {"groupId", groupId}
                };

                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.ToListAsync(record => new
                {
                    UserId = record["userId"].As<string>(),
                    Role = record["role"].As<string>()
                });
            });

            var members = new List<GroupMember>();

            foreach (var record in result)
            {
                var userName = await _userService.GetUserNameByIdAsync(record.UserId);
                var avatarUrl = await _userService.FetchUserAvatarUrlByIdAsync(record.UserId);

                var member = new GroupMember
                {
                    Id = record.UserId,
                    UserName = userName,
                    AvatarUrl = avatarUrl,
                    Role = record.Role
                };

                members.Add(member);
            }

            return members;
        }
    }

    public async Task<List<string>> GetGroupManagersAsync(string studyGroupId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var query = @"
                MATCH (sg:StudyGroup {id: $studyGroupId})<-[:MEMBER_OF {role: 'manager'}]-(u:User)
                RETURN u.id AS managerId";

                var parameters = new { studyGroupId };
                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.ToListAsync(record => record["managerId"].As<string>());
            });

            return result;
        }
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

        // // Check if the user is the leader of the group
        // if (studyGroup.LeaderId != userId)
        // {
        //     // User is not the leader, so they cannot delete the group
        //     return false;
        // }

        // Logic to delete the study group
        await DeleteStudyGroupFromDatabaseAsync(groupId);

        return true; // Return true if deletion is successful
    }

    public async Task<StudyGroup> GetStudyGroupByIdAsync(string groupId)
    {
        var entity = await _context.StudyGroups.FindAsync(groupId);
        if (entity == null) return null;

        var members = await GetStudyGroupMembers(groupId);
        return new StudyGroup
        {
            Id = entity.Id.ToString(),
            Name = entity.Name,
            Description = entity.Description,
            MemberIds = members,
            Status = entity.Status
        };
    }

    public async Task<List<StudyGroup>> SearchStudyGroups(string query, int skip, int take)
    {
        var sqlGroups = await _context.StudyGroups
            .Where(g => EF.Functions.Like(g.Name.ToLower(), $"%{query.ToLower()}%") && g.Status == "approved")
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
        var studyGroup = await _context.StudyGroups.FindAsync(groupId);
        if (studyGroup != null)
        {
            _context.StudyGroups.Remove(studyGroup);
            await _context.SaveChangesAsync();
        }

        // Delete the study group from Neo4j database
        using (var session = _neo4jDriver.AsyncSession())
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                var query = @"
            MATCH (s:StudyGroup {id: $groupId})
            OPTIONAL MATCH (s)-[r]-()
            DETACH DELETE s, r";
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
        var session = _neo4jDriver.AsyncSession();
        try
        {
            var cursor = await session.RunAsync(
                "MATCH (m:User)-[r:MEMBER_OF {role: 'manager'}]->(sg:StudyGroup {id: $studyGroupId}) " +
                "RETURN m.id AS managerId, sg.name AS studyGroupName",
                new { studyGroupId });

            var managerIds = await cursor.ToListAsync(record => record["managerId"].As<string>());
            var studyGroupName = await cursor.SingleAsync(record => record["studyGroupName"].As<string>());

            if (managerIds.Count > 0)
            {
                // Send notification using SignalR's SendNotificationToUsers method from ChatHub
                var notificationContent = $"User {userId} has applied to join your study group {studyGroupName}.";
                var notificationType = "StudyGroupApplication";
                // Only the URL to the join-request page is stored in the Data field
                var notificationUrl = $"/studygroup/{studyGroupId}";

                // Use the existing SendNotificationToUsers method from ChatHub
                var chatHub = new ChatHub(_context); // Assuming _context is your ApplicationDbContext
                await chatHub.SendNotificationToUsers(managerIds, notificationContent, notificationType, notificationUrl);
            }
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    public async Task<bool> UpdateApplicationStatusAsync(string userId, string studyGroupId, string status)
    {
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
                return await JoinGroupAsync(studyGroupId, userId);
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
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteWriteAsync(async tx =>
            {
                var checkQuery = @"
                MATCH (s:StudyGroup {id: $groupId})
                MATCH (u:User {id: $userId})
                RETURN EXISTS((u)-[:MEMBER_OF]->(s))";
                var checkParameters = new Dictionary<string, object>
                {
                    {"groupId", groupId},
                    {"userId", userId}
                };

                var checkCursor = await tx.RunAsync(checkQuery, checkParameters);
                var isMember = await checkCursor.SingleAsync(record => record[0].As<bool>());

                if (!isMember)
                {
                    var query = @"
                    MATCH (s:StudyGroup {id: $groupId})
                    MATCH (u:User {id: $userId})
                    MERGE (u)-[:MEMBER_OF {role: 'member', joinedAt: $joinedAt}]->(s)";
                    var parameters = new Dictionary<string, object>
                    {
                        {"groupId", groupId},
                        {"userId", userId},
                        {"joinedAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")}
                    };

                    var cursor = await tx.RunAsync(query, parameters);
                    return await cursor.FetchAsync();
                }

                return false;
            });

            return result;
        }
    }

    public async Task<bool> LeaveStudyGroup(string userId, string groupId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            return await session.ExecuteWriteAsync(async tx =>
            {
                // Check if the user is a manager
                var checkManagerQuery = @"
                MATCH (u:User {id: $userId})-[r:MANAGES]->(sg:StudyGroup {id: $groupId})
                RETURN COUNT(r) AS isManager";

                var managerCursor = await tx.RunAsync(checkManagerQuery, new { userId, groupId });
                var managerResult = await managerCursor.SingleAsync();
                var isManager = managerResult["isManager"].As<int>() > 0;

                if (isManager)
                {
                    // User is the manager, they cannot leave the group
                    throw new InvalidOperationException("Managers cannot leave their own study group. You must dissolve the group.");
                }

                // If the user is not the manager, allow them to leave the group
                var leaveGroupQuery = @"
                MATCH (u:User {id: $userId})-[r:MEMBER_OF]->(sg:StudyGroup {id: $groupId})
                DELETE r
                RETURN COUNT(r) AS removedCount";

                var leaveCursor = await tx.RunAsync(leaveGroupQuery, new { userId, groupId });
                var leaveResult = await leaveCursor.SingleAsync();
                var removedCount = leaveResult["removedCount"].As<int>();

                return removedCount > 0;
            });
        }
    }

    public async Task<bool> DissolveStudyGroup(string userId, string groupId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            return await session.ExecuteWriteAsync(async tx =>
            {
                // Check if the user is the manager of the study group
                var checkManagerQuery = @"
                MATCH (u:User {id: $userId})-[r:MANAGES]->(sg:StudyGroup {id: $groupId})
                RETURN COUNT(r) AS isManager";

                var managerCursor = await tx.RunAsync(checkManagerQuery, new { userId, groupId });
                var managerResult = await managerCursor.SingleAsync();
                var isManager = managerResult["isManager"].As<int>() > 0;

                if (!isManager)
                {
                    // User is not the manager, so they cannot dissolve the group
                    throw new UnauthorizedAccessException("Only the manager can dissolve the study group.");
                }

                // If the user is the manager, dissolve the group
                // Check if the study group exists
                var studyGroup = await GetStudyGroupByIdAsync(groupId);
                if (studyGroup == null)
                {
                    throw new InvalidOperationException("Study group does not exist.");
                }

                await DeleteStudyGroupFromDatabaseAsync(groupId);

                return true;  // Successfully dissolved the group
            });
        }
    }

    internal async Task<List<StudyGroup>> GetStudyGroupByUser(string userId, string currentUserId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var query = @"
            MATCH (u:User {id: $userId})-[r:MEMBER_OF]->(s:StudyGroup)
            RETURN s, r.role as role";

                var parameters = new Dictionary<string, object>
                {
                {"userId", userId},
                {"currentUserId", currentUserId}
                };

                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.ToListAsync();
            });

            var studyGroups = new List<StudyGroup>();
            foreach (var record in result)
            {
                // Assuming 's' is a node returned in the record
                var studyGroupNode = record["s"].As<INode>();
                var studyGroupId = studyGroupNode.Properties["id"].As<string>();

                // Fetch the members' info from the connected user node
                var members = await GetStudyGroupMembers(studyGroupId);

                // Fetch group info from sql
                var groupEntity = await _context.StudyGroups.FindAsync(Guid.Parse(studyGroupId));
                if (groupEntity == null) continue; // Skip if not found

                var studyGroup = new StudyGroup
                {
                    Id = groupEntity.Id.ToString(),
                    Name = groupEntity.Name,
                    Description = groupEntity.Description,
                    MemberIds = members,
                    Status = groupEntity.Status,
                    Role = record["role"].As<string>()
                };

                studyGroups.Add(studyGroup);
            }

            return studyGroups;
        }
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
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteWriteAsync(async tx =>
            {
                var query = @"
                MATCH (s:StudyGroup {id: $studyGroupId})
                MATCH (u:User {id: $memberId})
                MERGE (u)-[:MEMBER_OF {role: 'member', joinedAt: $joinedAt}]->(s)";
                var parameters = new Dictionary<string, object>
                {
                {"studyGroupId", studyGroupId},
                {"memberId", memberId},
                {"joinedAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")}
                };

                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.FetchAsync();
            });

            return result;
        }
    }

    public async Task<bool> ApproveJoinRequestAsync(string studyGroupId, string memberId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteWriteAsync(async tx =>
            {
                var query = @"
                MATCH (u:User {id: $memberId})-[r:APPLIED_TO]->(s:StudyGroup {id: $studyGroupId})
                DELETE r
                CREATE (u)-[:MEMBER_OF {role: 'member', joinedAt: $joinedAt}]->(s)";
                var parameters = new Dictionary<string, object>
                {
                {"studyGroupId", studyGroupId},
                {"memberId", memberId},
                {"joinedAt", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")}
                };

                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.FetchAsync();
            });

            return result;
        }
    }

    public async Task<bool> DeleteMemberAsync(string studyGroupId, string memberId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteWriteAsync(async tx =>
            {
                var query = @"
                MATCH (u:User {id: $memberId})-[r:MEMBER_OF]->(s:StudyGroup {id: $studyGroupId})
                DELETE r";
                var parameters = new Dictionary<string, object>
                {
                {"studyGroupId", studyGroupId},
                {"memberId", memberId}
                };

                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.FetchAsync();
            });

            return result;
        }
    }

    public async Task<bool> TransferManagerRoleAsync(string studyGroupId, string newManagerId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteWriteAsync(async tx =>
            {
                var query = @"
                MATCH (u:User)-[r:MEMBER_OF {role: 'manager'}]->(s:StudyGroup {id: $studyGroupId})
                MATCH (newManager:User {id: $newManagerId})-[r2:MEMBER_OF {role: 'member'}]->(s)
                SET r2.role = 'manager'";
                var parameters = new Dictionary<string, object>
                {
                {"studyGroupId", studyGroupId},
                {"newManagerId", newManagerId}
                };

                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.FetchAsync();
            });

            return result;
        }
    }

    public async Task<bool> RenameGroupAsync(string studyGroupId, string newName)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteWriteAsync(async tx =>
            {
                var query = @"
                MATCH (s:StudyGroup {id: $studyGroupId})
                SET s.name = $newName";
                var parameters = new Dictionary<string, object>
                {
                {"studyGroupId", studyGroupId},
                {"newName", newName}
                };

                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.FetchAsync();
            });

            return result;
        }
    }

    public async Task<bool> EditDescriptionAsync(string studyGroupId, string newDescription)
    {
        var group = await _context.StudyGroups.FindAsync(Guid.Parse(studyGroupId));
        if (group == null) return false;

        group.Description = newDescription;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> EditProfilePictureAsync(string studyGroupId, string newProfilePictureUrl)
    {
        var group = await _context.StudyGroups.FindAsync(Guid.Parse(studyGroupId));
        if (group == null) return false;

        group.ImageUrl = newProfilePictureUrl;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> IsUserManagerAsync(string studyGroupId, string userId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var query = @"
                    MATCH (u:User {id: $userId})-[r:MEMBER_OF {role: 'manager'}]->(s:StudyGroup {id: $studyGroupId})
                    RETURN COUNT(r) > 0 AS isManager";
                var parameters = new Dictionary<string, object>
                {
                    {"studyGroupId", studyGroupId},
                    {"userId", userId}
                };

                var cursor = await tx.RunAsync(query, parameters);
                var record = await cursor.SingleAsync();
                return record["isManager"].As<bool>();
            });

            return result;
        }
    }

    public async Task<string> GetUserRoleInGroupAsync(string groupId, string userId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var query = @"
                MATCH (u:User {id: $userId})-[r:MEMBER_OF]->(s:StudyGroup {id: $groupId})
                RETURN r.role AS role";
                var parameters = new Dictionary<string, object>
                {
                {"groupId", groupId},
                {"userId", userId}
                };

                var cursor = await tx.RunAsync(query, parameters);
                var records = await cursor.ToListAsync();

                var record = records.SingleOrDefault(); // Handle potential null
                return record?["role"].As<string>();
            });

            return result;
        }
    }

    public async Task<IEnumerable<JoinRequest>> GetJoinRequests(string groupId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var query = @"
                MATCH (u:User)-[r:APPLIED_TO]->(s:StudyGroup {id: $groupId})
                RETURN u.id AS userId, r.appliedOn AS appliedOn";
                var parameters = new { groupId };
                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.ToListAsync(record => new
                {
                    UserId = record["userId"].As<string>(),
                    AppliedOn = record["appliedOn"].As<string>()
                });
            });

            var joinRequests = new List<JoinRequest>();

            foreach (var record in result)
            {
                // Sequentially fetching UserName and AvatarUrl
                var userName = await _userService.GetUserNameByIdAsync(record.UserId);
                var avatarUrl = await _userService.FetchUserAvatarUrlByIdAsync(record.UserId);

                var joinRequest = new JoinRequest
                {
                    UserId = record.UserId,
                    Name = userName,
                    AvatarUrl = avatarUrl,
                    AppliedOn = record.AppliedOn
                };

                joinRequests.Add(joinRequest);
            }

            return joinRequests;
        }
    }

    public async Task<int> GetPendingJoinRequestsCount(string groupId)
    {
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

            return result;
        }
    }

    public async Task<IEnumerable<ActivityLog>> GetActivityLogs(string groupId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var query = @"
                MATCH (s:StudyGroup {id: $groupId})-[:HAS_LOG]->(l:ActivityLog)
                RETURN l.message AS message, l.date AS date";
                var parameters = new { groupId };
                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.ToListAsync(record => new ActivityLog
                {
                    Message = record["message"].As<string>(),
                    Date = record["date"].As<string>()
                });
            });

            return result;
        }
    }
}