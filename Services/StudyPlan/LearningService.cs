using Neo4j.Driver;
using Sciencetopia.Services.Plans;

namespace Sciencetopia.Services;

public class LearningService
{
    private readonly IDriver _driver;
    private readonly IPersonalPlanEnrollmentService _personalGroups;

    public LearningService(IDriver driver, IPersonalPlanEnrollmentService personalGroups)
    {
        _driver = driver;
        _personalGroups = personalGroups;
    }

    [Obsolete("Use IResourceProgressService.ToggleByLinkAsync for unified progress tracking.")]
    public async Task<bool> ToggleFinishedLearningRelationship(string resourceLink, string userId, string? source, string? device)
    {
        var personalGroupId = await _personalGroups.EnsurePersonalGroupProjectionAsync(userId);
        using (var session = _driver.AsyncSession())
        {
            var queryCheck = @"
            MATCH (g:Group {id: $groupId})-[rel:COMPLETED]->(r:Resource)
            WHERE r.link = $resourceLink
            RETURN rel";

            var queryDelete = @"
            MATCH (g:Group {id: $groupId})-[rel:COMPLETED]->(r:Resource)
            WHERE r.link = $resourceLink
            DELETE rel";

            var queryCreate = @"
            MERGE (g:Group {id: $groupId})
            SET g.kind = 'PersonalGroup'
            WITH g
            MATCH (r:Resource {link: $resourceLink})
            CREATE (g)-[rel:COMPLETED { at: datetime(), source: $source, device: $device }]->(r)
            RETURN rel";

            var parameters = new { resourceLink, groupId = personalGroupId.ToString(), source, device };

            var result = await session.RunAsync(queryCheck, parameters);
            var relationshipExists = await result.FetchAsync();

            if (relationshipExists)
            {
                await session.RunAsync(queryDelete, parameters);
            }
            else
            {
                await session.RunAsync(queryCreate, parameters);
            }

            return true;
        }
    }

    // public async Task<List<FinishedLessonDTO>> GetFinishedLearning(string userId)
    // {
    //     var finishedLearningList = new List<FinishedLessonDTO>();
    //     try
    //     {
    //         using (var session = _driver.AsyncSession())
    //         {
    //             var query = @"
    //                 MATCH (l:Lesson)-[r:HAS_RESOURCE]->(res:Resource)
    //                 OPTIONAL MATCH (l)-[fl:FINISHED_LEARNING]->(finishedRes:Resource)
    //                 WITH l, 
    //                 COLLECT(DISTINCT res) AS resources, 
    //                 COUNT(DISTINCT res) AS totalResources, 
    //                 COLLECT(DISTINCT finishedRes) AS finishedResources
    //                 RETURN l AS lesson, 
    //                 resources, 
    //                 totalResources, 
    //                 SIZE(finishedResources) AS finishedResources, 
    //                 (toFloat(SIZE(finishedResources)) / totalResources) * 100 AS finishedPercentage
    //                 ";

    //             var result = await session.RunAsync(query, new { userId });

    //             await foreach (var record in result)
    //             {
    //                 var lessonNode = record["lesson"].As<INode>();
    //                 var resourcesNodes = record["resources"].As<List<INode>>();
    //                 var totalResources = record["totalResources"].As<int>();
    //                 var finishedResources = record["finishedResources"].As<int>();
    //                 var finishedPercentage = record["finishedPercentage"].As<float>();

    //                 var lessonDTO = new FinishedLessonDTO
    //                 {
    //                     LessonName = lessonNode.Properties["name"].As<string>(),
    //                     ResourceLinks = resourcesNodes.Select(r => r.Properties["link"].As<string>()).ToList(),
    //                     TotalResources = totalResources,
    //                     FinishedResources = finishedResources,
    //                     FinishedPercentage = finishedPercentage
    //                 };

    //                 finishedLearningList.Add(lessonDTO);
    //             }
    //         }
    //     }
    //     catch (Exception ex)
    //     {
    //         // Log the exception or handle it as needed
    //         Console.WriteLine($"An error occurred: {ex.Message}");
    //     }

    //     return finishedLearningList;
    // }
}
