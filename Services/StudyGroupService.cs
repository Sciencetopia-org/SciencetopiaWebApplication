using Neo4j.Driver;
using Sciencetopia.Models;

public class StudyGroupService
{
    private readonly IDriver _neo4jDriver;

    public StudyGroupService(IDriver neo4jDriver)
    {
        _neo4jDriver = neo4jDriver;
    }

    public async Task<bool> CreateStudyGroupAsync(StudyGroupDTO studyGroupDTO, string userId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            try
            {
                if (studyGroupDTO.Name != null)
                {
                    var studyGroupExists = await CheckStudyGroupExistsAsync(studyGroupDTO.Name);
                    if (studyGroupExists)
                    {
                        // Study group with the same name already exists
                        return false;
                    }
                }

                var result = await session.ExecuteWriteAsync(async tx =>
                {
                    // Step 1: Create the StudyGroup node with status 'pending_approval'
                    var createGroupQuery = @"
                    CREATE (s:StudyGroup {id: randomUUID(), name: $studyGroupName, description: $studyGroupDescription, status: 'pending_approval'})
                    RETURN s.id AS groupId";
                    var groupParams = new Dictionary<string, object>
                    {
                    {"studyGroupName", studyGroupDTO.Name ?? string.Empty},
                    {"studyGroupDescription", studyGroupDTO.Description ?? string.Empty}
                    };
                    var groupResult = await tx.RunAsync(createGroupQuery, groupParams);
                    var groupIdRecord = await groupResult.SingleAsync();
                    var groupId = groupIdRecord["groupId"].As<string>();

                    // Step 2: Create the [:MEMBER_OF] relationship and assign a manager role to it
                    var createRelationQuery = @"
                    MATCH (u:User {id: $userId}), (s:StudyGroup {id: $groupId})
                    CREATE (u)-[:MEMBER_OF {role: 'manager'}]->(s)";
                    var relationParams = new Dictionary<string, object>
                    {
                    {"userId", userId},
                    {"groupId", groupId}
                    };
                    await tx.RunAsync(createRelationQuery, relationParams);

                    // Notify the administrator for approval (implementation depends on your notification system)

                    return groupId != null;
                });

                return result;
            }
            catch (Exception)
            {
                // Log the exception here
                return false;
            }
        }
    }

    public async Task<bool> ApproveStudyGroupAsync(string groupId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            try
            {
                var result = await session.ExecuteWriteAsync(async tx =>
                {
                    // Step 1: Update the StudyGroup node status to 'approved'
                    var updateGroupStatusQuery = @"
                    MATCH (s:StudyGroup {id: $groupId})
                    SET s.status = 'approved'
                    RETURN s.id AS groupId";
                    var updateGroupStatusParams = new Dictionary<string, object>
                    {
                    {"groupId", groupId}
                    };
                    var updateResult = await tx.RunAsync(updateGroupStatusQuery, updateGroupStatusParams);
                    var updateRecord = await updateResult.SingleAsync();
                    var updatedGroupId = updateRecord["groupId"].As<string>();

                    return updatedGroupId != null;
                });

                return result;
            }
            catch (Exception)
            {
                // Log the exception here
                return false;
            }
        }
    }

    public async Task<bool> RejectStudyGroupAsync(string groupId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            try
            {
                var result = await session.ExecuteWriteAsync(async tx =>
                {
                    // Step 1: Update the StudyGroup node status to 'rejected'
                    var updateGroupStatusQuery = @"
                    MATCH (s:StudyGroup {id: $groupId})
                    SET s.status = 'rejected'
                    RETURN s.id AS groupId";
                    var updateGroupStatusParams = new Dictionary<string, object>
                    {
                    {"groupId", groupId}
                    };
                    var updateResult = await tx.RunAsync(updateGroupStatusQuery, updateGroupStatusParams);
                    var updateRecord = await updateResult.SingleAsync();
                    var updatedGroupId = updateRecord["groupId"].As<string>();

                    return updatedGroupId != null;
                });

                return result;
            }
            catch (Exception)
            {
                // Log the exception here
                return false;
            }
        }
    }

    // public async Task<StudyGroup> GetStudyGroupById(string groupId)
    // {
    //     using (var session = _neo4jDriver.AsyncSession())
    //     {
    //         var result = await session.ExecuteReadAsync(async tx =>
    //         {
    //             var query = @"
    //                 MATCH (s:StudyGroup {id: $groupId})
    //                 RETURN s";
    //             var parameters = new Dictionary<string, object>
    //             {
    //                 {"groupId", groupId}
    //             };

    //             var cursor = await tx.RunAsync(query, parameters);
    //             return await cursor.ToListAsync();
    //         });

    //         var studyGroup = new StudyGroup();
    //         foreach (var record in result)
    //         {
    //             // Assuming 's' is a node returned in the record
    //             var studyGroupNode = record["s"].As<INode>();

    //             studyGroup.Id = studyGroupNode.Properties["id"].As<string>();
    //             studyGroup.Name = studyGroupNode.Properties["name"].As<string>();
    //             studyGroup.Description = studyGroupNode.Properties["description"].As<string>();
    //         }

    //         return studyGroup;
    //     }
    // }

    public async Task<List<StudyGroup>> GetAllStudyGroups()
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var query = @"
                    MATCH (s:StudyGroup)
                    RETURN s";

                var cursor = await tx.RunAsync(query);
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

                var studyGroup = new StudyGroup
                {
                    Id = studyGroupId,
                    Name = studyGroupNode.Properties["name"].As<string>(),
                    Description = studyGroupNode.Properties["description"].As<string>(),
                    MemberIds = members
                };
                studyGroups.Add(studyGroup);
            }

            return studyGroups;
        }
    }

    public async Task<List<GroupMember>> GetStudyGroupMembers(string groupId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var query = @"
                MATCH (u:User)-[r:MEMBER_OF]->(s:StudyGroup {id: $groupId})
                RETURN u, r.role as role";
                var parameters = new Dictionary<string, object>
                {
                {"groupId", groupId}
                };

                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.ToListAsync();
            });

            var members = new List<GroupMember>();
            foreach (var record in result)
            {
                // Assuming 'u' is a node returned in the record
                var userNode = record["u"].As<INode>();

                var user = new GroupMember
                {
                    Id = userNode.Properties["id"].As<string>(),
                    Role = record["role"].As<string>()  // Add role to the member info
                };
                members.Add(user);
            }

            return members;
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
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var query = @"
            MATCH (s:StudyGroup {id: $groupId})
            RETURN s";
                var parameters = new Dictionary<string, object>
                {
                {"groupId", groupId}
                };

                var cursor = await tx.RunAsync(query, parameters);
                var records = await cursor.ToListAsync();
                var record = records.SingleOrDefault(); // Using LINQ SingleOrDefault on the list
                if (record == null)
                {
                    return null;
                }

                var studyGroupNode = record["s"].As<INode>();
                var studyGroupId = studyGroupNode.Properties["id"].As<string>();

                // Fetch the members' info from the connected user node
                var members = await GetStudyGroupMembers(studyGroupId);
                //     var memberQuery = @"
                // MATCH (u:User)-[:MEMBER_OF]->(s:StudyGroup)
                // WHERE s.id = $groupId
                // RETURN u";
                //     var memberCursor = await tx.RunAsync(memberQuery, parameters);
                //     var memberRecords = await memberCursor.ToListAsync();
                //     var memberRecord = memberRecords.SingleOrDefault(); // Consider handling null here

                //     var memberNode = memberRecord?["u"].As<INode>();

                return new StudyGroup
                {
                    Id = studyGroupId,
                    Name = studyGroupNode.Properties["name"].As<string>(),
                    Description = studyGroupNode.Properties["description"].As<string>(),
                    MemberIds = members,
                    // MemberId = memberNode?.Properties["id"].As<string>() // Handle potential null
                };
            });

            return result ?? throw new Exception("Result is null.");
        }
    }

    private async Task DeleteStudyGroupFromDatabaseAsync(string groupId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            await session.ExecuteWriteAsync(async tx =>
            {
                var query = @"
            MATCH (s:StudyGroup {id: $groupId})
            DETACH DELETE s";
                var parameters = new Dictionary<string, object>
                {
                {"groupId", groupId}
                };

                await tx.RunAsync(query, parameters);
            });
        }
    }

    private async Task<bool> CheckStudyGroupExistsAsync(string studyGroupName)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var query = @"
            MATCH (s:StudyGroup {name: $studyGroupName})
            RETURN s";
                var parameters = new Dictionary<string, object>
                {
                {"studyGroupName", studyGroupName}
                };

                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.FetchAsync();
            });

            return result;
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
            return result;
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
                    MERGE (u)-[:MEMBER_OF]->(s)";
                    var parameters = new Dictionary<string, object>
                    {
                        {"groupId", groupId},
                        {"userId", userId}
                    };

                    var cursor = await tx.RunAsync(query, parameters);
                    return await cursor.FetchAsync();
                }

                return false;
            });

            return result;
        }
    }

    internal async Task<List<StudyGroup>> GetStudyGroupByUser(string userId)
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
                {"userId", userId}
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

                var studyGroup = new StudyGroup
                {
                    Id = studyGroupId,
                    Name = studyGroupNode.Properties["name"].As<string>(),
                    Description = studyGroupNode.Properties["description"].As<string>(),
                    MemberIds = members,
                    Role = record["role"].As<string>() // Set the role property here
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
                MERGE (u)-[:MEMBER_OF {role: 'member'}]->(s)";
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

    public async Task<bool> ApproveJoinRequestAsync(string studyGroupId, string memberId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteWriteAsync(async tx =>
            {
                var query = @"
                MATCH (u:User {id: $memberId})-[r:APPLIED_TO]->(s:StudyGroup {id: $studyGroupId})
                DELETE r
                CREATE (u)-[:MEMBER_OF {role: 'member'}]->(s)";
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
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteWriteAsync(async tx =>
            {
                var query = @"
                MATCH (s:StudyGroup {id: $studyGroupId})
                SET s.description = $newDescription";
                var parameters = new Dictionary<string, object>
                {
                {"studyGroupId", studyGroupId},
                {"newDescription", newDescription}
                };

                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.FetchAsync();
            });

            return result;
        }
    }

    public async Task<bool> EditProfilePictureAsync(string studyGroupId, string newProfilePictureUrl)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteWriteAsync(async tx =>
            {
                var query = @"
                MATCH (s:StudyGroup {id: $studyGroupId})
                SET s.profilePictureUrl = $newProfilePictureUrl";
                var parameters = new Dictionary<string, object>
                {
                {"studyGroupId", studyGroupId},
                {"newProfilePictureUrl", newProfilePictureUrl}
                };

                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.FetchAsync();
            });

            return result;
        }
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
                RETURN u.id AS userId, u.name AS name, r.appliedOn AS appliedOn";
                var parameters = new { groupId };
                var cursor = await tx.RunAsync(query, parameters);
                return await cursor.ToListAsync(record => new JoinRequest
                {
                    UserId = record["userId"].As<string>(),
                    Name = record["name"].As<string>(),
                    AppliedOn = record["appliedOn"].As<string>()
                });
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