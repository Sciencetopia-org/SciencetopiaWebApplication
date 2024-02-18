using Neo4j.Driver;

public class LearningService
{
    private readonly IDriver _driver;

    public LearningService(IDriver driver)
    {
        _driver = driver;
    }

    public async Task<bool> ToggleFinishedLearningRelationship(string lessonName, string resourceLink, string userId)
    {
        using (var session = _driver.AsyncSession())
        {
            var queryCheck = @"
            MATCH (l:Lesson)-[rel:FINISHED_LEARNING]->(r:Resource)
            WHERE l.name = $lessonName AND r.link = $resourceLink AND rel.userId = $userId
            RETURN rel";

            var queryDelete = @"
            MATCH (l:Lesson)-[rel:FINISHED_LEARNING]->(r:Resource)
            WHERE l.name = $lessonName AND r.link = $resourceLink AND rel.userId = $userId
            DELETE rel";

            var queryCreate = @"
            MATCH (l:Lesson), (r:Resource)
            WHERE l.name = $lessonName AND r.link = $resourceLink
            CREATE (l)-[rel:FINISHED_LEARNING { userId: $userId }]->(r)
            RETURN rel";

            var parameters = new { lessonName, resourceLink, userId };

            // Check if the relationship exists
            var result = await session.RunAsync(queryCheck, parameters);
            var relationshipExists = await result.FetchAsync();

            if (relationshipExists)
            {
                // If the relationship exists, delete it
                await session.RunAsync(queryDelete, parameters);
            }
            else
            {
                // If the relationship does not exist, create it
                await session.RunAsync(queryCreate, parameters);
            }

            return true; // Or you can return the result based on your logic
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
