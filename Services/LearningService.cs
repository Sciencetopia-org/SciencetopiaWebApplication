using Neo4j.Driver;

public class LearningService
{
    private readonly IDriver _driver;

    public LearningService(IDriver driver)
    {
        _driver = driver;
    }

    public async Task<bool> CreateFinishedLearningRelationship(string lessonName, string resourceLink, string userId)
    {
        using (var session = _driver.AsyncSession())
        {
            var query = @"
                MATCH (l:Lesson), (r:Resource)
                WHERE l.name = $lessonName AND r.link = $resourceLink
                CREATE (l)-[rel:FINISHED_LEARNING { userId: $userId }]->(r)
                RETURN rel";

            var parameters = new { lessonName, resourceLink, userId };

            await session.RunAsync(query, parameters);

            return true; // Or you can return the result based on your logic
        }
    }

    public async Task<List<FinishedLessonDTO>> GetFinishedLearning(string userId)
    {
        var finishedLearningList = new List<FinishedLessonDTO>();
        try
        {
            using (var session = _driver.AsyncSession())
            {
                var query = @"
                MATCH (l:Lesson)-[rel:FINISHED_LEARNING]->(r:Resource)
                WHERE rel.userId = $userId
                RETURN l AS lesson, collect(r) AS resources";

                var result = await session.RunAsync(query, new { userId });

                await foreach (var record in result)
                {
                    var lessonNode = record["lesson"].As<INode>();
                    var resourcesNodes = record["resources"].As<List<INode>>();

                    var lessonDTO = new FinishedLessonDTO
                    {
                        LessonName = lessonNode.Properties["name"].As<string>(),
                        ResourceLinks = resourcesNodes.Select(r => r.Properties["link"].As<string>()).ToList()
                    };

                    finishedLearningList.Add(lessonDTO);
                }
            }
        }
        catch (Exception ex)
        {
            // Log the exception or handle it as needed
            Console.WriteLine($"An error occurred: {ex.Message}");
        }

        return finishedLearningList;
    }
}
