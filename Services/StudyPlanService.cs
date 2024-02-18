using Neo4j.Driver;
using System.Linq;
using Sciencetopia.Models;
using Microsoft.Extensions.Logging;

public class StudyPlanService
{
    private readonly IDriver _neo4jDriver;
    private readonly ILogger<StudyPlanService> _logger;

    public StudyPlanService(IDriver neo4jDriver, ILogger<StudyPlanService> logger)
    {
        _neo4jDriver = neo4jDriver;
        _logger = logger;
    }

    public async Task<bool> SaveStudyPlanAsync(StudyPlanDTO studyPlanDTO, string userId)
    {
        var session = _neo4jDriver.AsyncSession();
        try
        {
            // Check if a study plan with the same title already exists
            var existingPlanCheck = await session.ExecuteReadAsync(async transaction =>
            {
                var result = await transaction.RunAsync($@"
                    MATCH (u:User {{id: $userId}})-[:CREATED]->(p:StudyPlan {{title: $title}})
                    RETURN p", new { userId, title = studyPlanDTO.StudyPlan?.Title });

                // Fetch the results
                var record = await result.ToListAsync();

                // Check if any record exists
                return record.Any();
            });

            // If a record is found, return false indicating the plan already exists
            if (existingPlanCheck)
            {
                return false;
            }

            // If no existing plan, proceed to create a new study plan
            await session.ExecuteWriteAsync(async transaction =>
            {
                var studyPlan = studyPlanDTO.StudyPlan;
                // Create or find the User node
                // Create the StudyPlan node
                var createPlanResult = await transaction.RunAsync($@"
                    CREATE (p:StudyPlan {{title: $title}})
                    RETURN p", new { title = studyPlan?.Title });

                // Connect the User to the StudyPlan
                await transaction.RunAsync($@"
                    MATCH (u:User {{id: $userId}}), (p:StudyPlan {{title: $title}})
                    MERGE (u)-[:CREATED]->(p)",
                    new { userId, title = studyPlan?.Title });

                // Create nodes for prerequisites and link them to the study plan
                foreach (var lesson in studyPlan.Prerequisite)
                {
                    await transaction.RunAsync($@"
                        MERGE (p:StudyPlan {{title: $title}})
                        MERGE (l:Lesson {{name: $name}})
                        ON CREATE SET l.description = $description
                        MERGE (p)-[:HAS_PREREQUISITE]->(l)",
                        new { title = studyPlan.Title, name = lesson.Name, description = lesson.Description });
                }

                // Create nodes for main curriculum and link them to the study plan
                foreach (var lesson in studyPlan.MainCurriculum)
                {
                    await transaction.RunAsync($@"
                        MERGE (p:StudyPlan {{title: $title}})
                        MERGE (l:Lesson {{name: $name}})
                        ON CREATE SET l.description = $description
                        MERGE (p)-[:HAS_MAIN_CURRICULUM]->(l)",
                        new { title = studyPlan.Title, name = lesson.Name, description = lesson.Description });
                }
            });

            return true; // Return true to indicate success
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    public async Task<List<StudyPlanDTO>> GetStudyPlansByUserIdAsync(string userId)
    {
        using var session = _neo4jDriver.AsyncSession();
        try
        {
            var studyPlanResults = new List<StudyPlanDTO>();
            var result = await session.RunAsync($@"
    MATCH (u:User {{id: $userId}})-[:CREATED]->(sp:StudyPlan)
    OPTIONAL MATCH (sp)-[:HAS_PREREQUISITE]->(pr:Lesson)
    OPTIONAL MATCH (pr)-[:HAS_RESOURCE]->(prRes:Resource)
    OPTIONAL MATCH (sp)-[:HAS_MAIN_CURRICULUM]->(mc:Lesson)
    OPTIONAL MATCH (mc)-[:HAS_RESOURCE]->(mcRes:Resource)
    WITH sp, pr, prRes, mc, mcRes, 
     EXISTS((pr)-[:FINISHED_LEARNING {{userId: u.id}}]->(prRes)) AS prLearned,
     EXISTS((mc)-[:FINISHED_LEARNING {{userId: u.id}}]->(mcRes)) AS mcLearned
    WITH sp, pr, mc, 
         collect(DISTINCT {{resource: prRes.link, learned: prLearned}}) AS prResources,
         collect(DISTINCT {{resource: mcRes.link, learned: mcLearned}}) AS mcResources
    RETURN sp AS StudyPlan, 
           collect(DISTINCT {{lesson: pr, resources: prResources}}) AS Prerequisites, 
           collect(DISTINCT {{lesson: mc, resources: mcResources}}) AS MainCurriculum
", new { userId });

            await foreach (var record in result)
            {
                var studyPlanNode = record["StudyPlan"].As<INode>();
                var prerequisitesData = record["Prerequisites"].As<List<object>>();
                var mainCurriculumData = record["MainCurriculum"].As<List<object>>();

                var prerequisiteLessons = TransformLessonsWithProgress(prerequisitesData);
                var mainCurriculumLessons = TransformLessonsWithProgress(mainCurriculumData);

                studyPlanResults.Add(new StudyPlanDTO
                {
                    StudyPlan = new StudyPlanDetail
                    {
                        Title = studyPlanNode.Properties["title"].As<string>(),
                        Prerequisite = prerequisiteLessons,
                        MainCurriculum = mainCurriculumLessons
                    }
                });
            }

            return studyPlanResults;
        }
        finally
        {
            await session.CloseAsync();
        }
    }

private List<Lesson> TransformLessonsWithProgress(List<object> lessonData)
{
    return lessonData.Select(data =>
    {
        var lessonDict = (Dictionary<string, object>)data;
        var lessonNode = lessonDict["lesson"] as INode;
        
        // Adjusted handling of resourcesData to accommodate List<object>
        var resourcesRawData = lessonDict["resources"] as List<object>;
        _logger.LogInformation("lessonDict: {lessonDict}", lessonDict);
        _logger.LogInformation("resourcesData: {resourcesData}", resourcesRawData);

        var resources = resourcesRawData?.Select(resRaw =>
        {
            var resDict = resRaw as Dictionary<string, object>; // Safely cast each resource
            if (resDict == null) return null; // Skip if the cast fails

            return new Resource
            {
                Link = resDict.ContainsKey("resource") ? resDict["resource"]?.ToString() : string.Empty,
                Learned = resDict.ContainsKey("learned") && Convert.ToBoolean(resDict["learned"])
            };
        }).Where(r => r != null).ToList(); // Filter out any null resources

        var finishedResourcesCount = resources.Count(r => r.Learned);
        var totalResources = resources.Count;
        var progressPercentage = totalResources > 0 ? (finishedResourcesCount / (float)totalResources) * 100 : 0;

        return new Lesson
        {
            Name = lessonNode.Properties["name"]?.As<string>(),
            Description = lessonNode.Properties["description"]?.As<string>(),
            Resources = resources,
            FinishedResourcesCount = finishedResourcesCount,
            ProgressPercentage = progressPercentage
        };
    }).ToList();
}


    public async Task<bool> DeleteStudyPlanAsync(string studyPlanTitle, string currentUserId)
    {
        var session = _neo4jDriver.AsyncSession();
        try
        {
            return await session.ExecuteWriteAsync(async transaction =>
            {
                var result = await transaction.RunAsync($@"
                    MATCH (u:User {{id: $currentUserId}})-[:CREATED]->(p:StudyPlan {{title: $title}})
                    DETACH DELETE p
                    RETURN COUNT(p) > 0",
                    new { currentUserId, title = studyPlanTitle });

                var summary = await result.ConsumeAsync();
                return summary.Counters.NodesDeleted > 0;
            });
        }
        finally
        {
            await session.CloseAsync();
        }
    }
}
