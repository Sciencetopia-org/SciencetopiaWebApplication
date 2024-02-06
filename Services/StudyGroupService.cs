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

                    // Step 2: Create the [:LEADER] relationship
                    var createRelationQuery = @"
                        MATCH (u:User {id: $userId}), (s:StudyGroup {id: $groupId})
                        CREATE (u)-[:LEADER]->(s)";
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

                var studyGroup = new StudyGroup
                {
                    Id = studyGroupNode.Properties["id"].As<string>(),
                    Name = studyGroupNode.Properties["name"].As<string>(),
                    Description = studyGroupNode.Properties["description"].As<string>()
                };
                studyGroups.Add(studyGroup);
            }

            return studyGroups;
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

        // Check if the user is the leader of the group
        if (studyGroup.LeaderId != userId)
        {
            // User is not the leader, so they cannot delete the group
            return false;
        }

        // Logic to delete the study group
        await DeleteStudyGroupFromDatabaseAsync(groupId);

        return true; // Return true if deletion is successful
    }

    private async Task<StudyGroup> GetStudyGroupByIdAsync(string groupId)
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

                // Fetch the leader's info from the connected user node
                var leaderQuery = @"
            MATCH (u:User)-[:LEADER]->(s)
            WHERE s.id = $groupId
            RETURN u";
                var leaderCursor = await tx.RunAsync(leaderQuery, parameters);
                var leaderRecords = await leaderCursor.ToListAsync();
                var leaderRecord = leaderRecords.SingleOrDefault(); // Consider handling null here

                var leaderNode = leaderRecord?["u"].As<INode>();

                return new StudyGroup
                {
                    Id = studyGroupNode.Properties["id"].As<string>(),
                    Name = studyGroupNode.Properties["name"].As<string>(),
                    Description = studyGroupNode.Properties["description"].As<string>(),
                    LeaderId = leaderNode?.Properties["id"].As<string>() // Handle potential null
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
    
    internal async Task<bool> JoinGroupAsync(string groupId, string userId)
    {
        using (var session = _neo4jDriver.AsyncSession())
        {
            var result = await session.ExecuteWriteAsync(async tx =>
            {
                var checkQuery = @"
                MATCH (s:StudyGroup {id: $groupId})
                MATCH (u:User {id: $userId})
                RETURN EXISTS((u)-[:MEMBER_OF|LEADER_OF]->(s))";
                var checkParameters = new Dictionary<string, object>
                {
                    {"groupId", groupId},
                    {"userId", userId}
                };

                var checkCursor = await tx.RunAsync(checkQuery, checkParameters);
                var isMemberOrLeader = await checkCursor.SingleAsync(record => record[0].As<bool>());

                if (!isMemberOrLeader)
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