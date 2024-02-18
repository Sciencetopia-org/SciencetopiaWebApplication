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
                    // Step 1: Create the StudyGroup node
                    var createGroupQuery = @"
                        CREATE (s:StudyGroup {id: randomUUID(), name: $studyGroupName, description: $studyGroupDescription})
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

                    return groupId != null;
                });

                return result != null;
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
                    MATCH (u:User)-[:MEMBER_OF]->(s:StudyGroup {id: $groupId})
                    RETURN u";
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
}