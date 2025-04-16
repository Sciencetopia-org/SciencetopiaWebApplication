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

                var record = await result.ToListAsync();
                return record.Any();
            });

            if (existingPlanCheck)
            {
                return false;
            }

            // Proceed to create a new study plan
            await session.ExecuteWriteAsync(async transaction =>
            {
                var studyPlan = studyPlanDTO.StudyPlan;
                var studyPlanId = Guid.NewGuid().ToString(); // Create unique study plan id

                // Create the StudyPlan node with a unique id
                await transaction.RunAsync($@"
            CREATE (p:StudyPlan {{id: $studyPlanId, title: $title, introduction: $introduction, keywords: $keywords}})
            RETURN p", new { studyPlanId, title = studyPlan?.Title, introduction = studyPlan?.Introduction?.Description, keywords = studyPlan?.Introduction?.Keywords });

                // Connect the User to the StudyPlan
                await transaction.RunAsync($@"
            MATCH (u:User {{id: $userId}}), (p:StudyPlan {{id: $studyPlanId}})
            MERGE (u)-[:CREATED]->(p)",
                    new { userId, studyPlanId });

                // Handle prerequisites
                foreach (var lesson in studyPlan.Prerequisite)
                {
                    var lessonId = Guid.NewGuid().ToString(); // Create unique lesson id

                    // Merge the lesson by id and set name and description
                    await transaction.RunAsync($@"
                MATCH (p:StudyPlan {{id: $studyPlanId}})
                MERGE (l:Lesson {{id: $lessonId}})
                ON CREATE SET l.name = $name, l.description = $description, l.keywords = $keywords
                MERGE (p)-[:HAS_PREREQUISITE]->(l)",
                        new { studyPlanId, lessonId, name = lesson.Name, description = lesson.Description, keywords = lesson.Keywords });

                    // Add resources for each prerequisite lesson
                    foreach (var resource in lesson.Resources)
                    {
                        await transaction.RunAsync($@"
                    MATCH (l:Lesson {{id: $lessonId}})
                    MERGE (r:Resource {{name: $resourceName, link: $resourceLink}})
                    MERGE (l)-[:HAS_RESOURCE]->(r)",
                            new { lessonId, resourceName = resource.Name, resourceLink = resource.Link });
                    }
                }

                // Handle main curriculum
                foreach (var lesson in studyPlan.MainCurriculum)
                {
                    var lessonId = Guid.NewGuid().ToString(); // Create unique lesson id

                    await transaction.RunAsync($@"
                MATCH (p:StudyPlan {{id: $studyPlanId}})
                MERGE (l:Lesson {{id: $lessonId}})
                ON CREATE SET l.name = $name, l.description = $description, l.keywords = $keywords
                MERGE (p)-[:HAS_MAIN_CURRICULUM]->(l)",
                        new { studyPlanId, lessonId, name = lesson.Name, description = lesson.Description, keywords = lesson.Keywords });

                    // Add resources for each main curriculum lesson
                    foreach (var resource in lesson.Resources)
                    {
                        await transaction.RunAsync($@"
                    MATCH (l:Lesson {{id: $lessonId}})
                    MERGE (r:Resource {{name: $resourceName, link: $resourceLink}})
                    MERGE (l)-[:HAS_RESOURCE]->(r)",
                            new { lessonId, resourceName = resource.Name, resourceLink = resource.Link });
                    }
                }

                // Handle advanced topics
                foreach (var lesson in studyPlan.AdvancedTopics)
                {
                    var lessonId = Guid.NewGuid().ToString(); // Create unique lesson id

                    await transaction.RunAsync($@"
                MATCH (p:StudyPlan {{id: $studyPlanId}})
                MERGE (l:Lesson {{id: $lessonId}})
                ON CREATE SET l.name = $name, l.description = $description, l.keywords = $keywords
                MERGE (p)-[:HAS_ADVANCED_TOPIC]->(l)",
                        new { studyPlanId, lessonId, name = lesson.Name, description = lesson.Description, keywords = lesson.Keywords });

                    // Add resources for each advanced topic lesson
                    foreach (var resource in lesson.Resources)
                    {
                        await transaction.RunAsync($@"
                    MATCH (l:Lesson {{id: $lessonId}})
                    MERGE (r:Resource {{name: $resourceName, link: $resourceLink}})
                    MERGE (l)-[:HAS_RESOURCE]->(r)",
                            new { lessonId, resourceName = resource.Name, resourceLink = resource.Link });
                    }
                }
            });

            return true; // Return true to indicate success
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    public async Task<bool> UpdateStudyPlanAsync(StudyPlanDTO updatedStudyPlan)
    {
        using var session = _neo4jDriver.AsyncSession();
        try
        {
            var studyPlanId = updatedStudyPlan.StudyPlan.Id;

            // Check if the study plan exists
            var existingPlanCheck = await session.ExecuteReadAsync(async transaction =>
            {
                var result = await transaction.RunAsync($@"
                MATCH (p:StudyPlan {{id: $studyPlanId}})
                RETURN p", new { studyPlanId });

                var recordList = await result.ToListAsync();
                return recordList.Any(); // Check if any records were returned
            });

            if (!existingPlanCheck)
            {
                _logger.LogError("StudyPlan with ID {studyPlanId} does not exist.", studyPlanId);
                return false; // Study plan does not exist
            }

            // Update the study plan details
            await session.ExecuteWriteAsync(async transaction =>
            {
                var studyPlan = updatedStudyPlan.StudyPlan;

                // Update the StudyPlan node (title and introduction)
                await transaction.RunAsync($@"
                MATCH (p:StudyPlan {{id: $studyPlanId}})
                SET p.title = $title, p.introduction = $introduction",
                    new { studyPlanId, title = studyPlan?.Title, introduction = studyPlan?.Introduction?.Description });

                // Delete old relationships (but not the lessons or resources themselves)
                await transaction.RunAsync($@"
                MATCH (p:StudyPlan {{id: $studyPlanId}})-[r:HAS_PREREQUISITE|HAS_MAIN_CURRICULUM|HAS_ADVANCED_TOPIC]->(l:Lesson)
                DELETE r", new { studyPlanId });

                // List of lesson IDs in the updated study plan
                var updatedLessonIds = new List<string>();

                // Update prerequisites
                foreach (var lesson in studyPlan.Prerequisite)
                {
                    var lessonId = lesson.Id ?? Guid.NewGuid().ToString();
                    updatedLessonIds.Add(lessonId); // Add to updated list

                    // Merge the lesson node by id and link it to the study plan
                    await transaction.RunAsync($@"
                    MATCH (p:StudyPlan {{id: $studyPlanId}})
                    MERGE (l:Lesson {{id: $lessonId}})
                    ON CREATE SET l.name = $name, l.description = $description
                    MERGE (p)-[:HAS_PREREQUISITE]->(l)",
                        new { studyPlanId, lessonId, name = lesson.Name, description = lesson.Description });

                    // Add resources for each prerequisite lesson
                    foreach (var resource in lesson.Resources)
                    {
                        if (!string.IsNullOrEmpty(resource.Name) && !string.IsNullOrEmpty(resource.Link))
                        {
                            await transaction.RunAsync($@"
                            MATCH (l:Lesson {{id: $lessonId}})
                            MERGE (r:Resource {{link: $resourceLink}})
                            ON CREATE SET r.name = $resourceName
                            MERGE (l)-[:HAS_RESOURCE]->(r)",
                                new { lessonId, resourceName = resource.Name, resourceLink = resource.Link });
                        }
                    }
                }

                // Update main curriculum
                foreach (var lesson in studyPlan.MainCurriculum)
                {
                    var lessonId = lesson.Id ?? Guid.NewGuid().ToString();
                    updatedLessonIds.Add(lessonId); // Add to updated list

                    // Merge the lesson node by id and link it to the study plan
                    await transaction.RunAsync($@"
                    MATCH (p:StudyPlan {{id: $studyPlanId}})
                    MERGE (l:Lesson {{id: $lessonId}})
                    ON CREATE SET l.name = $name, l.description = $description
                    MERGE (p)-[:HAS_MAIN_CURRICULUM]->(l)",
                        new { studyPlanId, lessonId, name = lesson.Name, description = lesson.Description });

                    // Add resources for each main curriculum lesson
                    foreach (var resource in lesson.Resources)
                    {
                        if (!string.IsNullOrEmpty(resource.Name) && !string.IsNullOrEmpty(resource.Link))
                        {
                            await transaction.RunAsync($@"
                            MATCH (l:Lesson {{id: $lessonId}})
                            MERGE (r:Resource {{link: $resourceLink}})
                            ON CREATE SET r.name = $resourceName
                            MERGE (l)-[:HAS_RESOURCE]->(r)",
                                new { lessonId, resourceName = resource.Name, resourceLink = resource.Link });
                        }
                    }
                }

                // Update advanced topics
                foreach (var lesson in studyPlan.AdvancedTopics)
                {
                    var lessonId = lesson.Id ?? Guid.NewGuid().ToString();
                    updatedLessonIds.Add(lessonId); // Add to updated list

                    // Merge the lesson node by id and link it to the study plan
                    await transaction.RunAsync($@"
                    MATCH (p:StudyPlan {{id: $studyPlanId}})
                    MERGE (l:Lesson {{id: $lessonId}})
                    ON CREATE SET l.name = $name, l.description = $description
                    MERGE (p)-[:HAS_ADVANCED_TOPIC]->(l)",
                        new { studyPlanId, lessonId, name = lesson.Name, description = lesson.Description });

                    // Add resources for each advanced topic lesson
                    foreach (var resource in lesson.Resources)
                    {
                        if (!string.IsNullOrEmpty(resource.Name) && !string.IsNullOrEmpty(resource.Link))
                        {
                            await transaction.RunAsync($@"
                            MATCH (l:Lesson {{id: $lessonId}})
                            MERGE (r:Resource {{link: $resourceLink}})
                            ON CREATE SET r.name = $resourceName
                            MERGE (l)-[:HAS_RESOURCE]->(r)",
                                new { lessonId, resourceName = resource.Name, resourceLink = resource.Link });
                        }
                    }
                }

                // Remove lessons not included in the updated study plan
                await transaction.RunAsync($@"
                MATCH (p:StudyPlan {{id: $studyPlanId}})-[r:HAS_PREREQUISITE|HAS_MAIN_CURRICULUM|HAS_ADVANCED_TOPIC]->(l:Lesson)
                WHERE NOT l.id IN $updatedLessonIds
                DETACH DELETE l", new { studyPlanId, updatedLessonIds });
            });

            return true; // Successfully updated
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update study plan");
            return false;
        }
        finally
        {
            await session.CloseAsync();
        }
    }
    public async Task<List<StudyPlanDTO>> GetStudyPlansByUserIdAsync(string currentUserId, string targetUserId)
    {
        using var session = _neo4jDriver.AsyncSession();
        try
        {
            var studyPlanResults = new List<StudyPlanDTO>();

            // Modify the query to include privacy checks
            var result = await session.RunAsync($@"
            MATCH (u:User {{id: $targetUserId}})-[:CREATED]->(sp:StudyPlan)
            WHERE sp.privacy = 'public' OR sp.privacy = 'shared' OR u.id = $currentUserId
            OPTIONAL MATCH (sp)-[:HAS_PREREQUISITE]->(pr:Lesson)
            OPTIONAL MATCH (pr)-[:HAS_RESOURCE]->(prRes:Resource)
            OPTIONAL MATCH (sp)-[:HAS_MAIN_CURRICULUM]->(mc:Lesson)
            OPTIONAL MATCH (mc)-[:HAS_RESOURCE]->(mcRes:Resource)
            OPTIONAL MATCH (sp)-[:HAS_ADVANCED_TOPIC]->(at:Lesson)
            OPTIONAL MATCH (at)-[:HAS_RESOURCE]->(atRes:Resource)
            WITH sp, pr, prRes, mc, mcRes, at, atRes,
                 EXISTS((pr)-[:FINISHED_LEARNING {{userId: u.id}}]->(prRes)) AS prLearned,
                 EXISTS((mc)-[:FINISHED_LEARNING {{userId: u.id}}]->(mcRes)) AS mcLearned,
                 EXISTS((at)-[:FINISHED_LEARNING {{userId: u.id}}]->(atRes)) AS atLearned
            WITH sp, pr, mc, at,
                 collect(DISTINCT {{resource: prRes.link, name: prRes.name, learned: prLearned}}) AS prResources,
                 collect(DISTINCT {{resource: mcRes.link, name: mcRes.name, learned: mcLearned}}) AS mcResources,
                 collect(DISTINCT {{resource: atRes.link, name: atRes.name, learned: atLearned}}) AS atResources
            RETURN sp AS StudyPlan, 
                   sp.id AS studyPlanId,
                   collect(DISTINCT {{lesson: pr, lessonId: pr.id, resources: prResources}}) AS Prerequisites, 
                   collect(DISTINCT {{lesson: mc, lessonId: mc.id, resources: mcResources}}) AS MainCurriculum,
                   collect(DISTINCT {{lesson: at, lessonId: at.id, resources: atResources}}) AS AdvancedTopics
        ", new { currentUserId, targetUserId });

            await foreach (var record in result)
            {
                var studyPlanNode = record["StudyPlan"].As<INode>();
                var studyPlanId = record["studyPlanId"].As<string>();
                var prerequisitesData = record["Prerequisites"].As<List<object>>();
                var mainCurriculumData = record["MainCurriculum"].As<List<object>>();
                var advancedTopicsData = record["AdvancedTopics"].As<List<object>>();

                // Transform data into Lesson objects
                var prerequisiteLessons = prerequisitesData != null ? TransformLessonsWithProgress(prerequisitesData) : new List<Lesson>();
                var mainCurriculumLessons = mainCurriculumData != null ? TransformLessonsWithProgress(mainCurriculumData) : new List<Lesson>();
                var advancedTopicsLessons = advancedTopicsData != null ? TransformLessonsWithProgress(advancedTopicsData) : new List<Lesson>();

                // Calculate total number of resources and learned resources for prerequisites and main curriculum
                var totalResources = prerequisiteLessons.Sum(l => l.Resources.Count) +
                                     mainCurriculumLessons.Sum(l => l.Resources.Count);

                var learnedResources = prerequisiteLessons.Sum(l => l.Resources.Count(r => r.Learned)) +
                                       mainCurriculumLessons.Sum(l => l.Resources.Count(r => r.Learned));

                // Calculate progress percentage for prerequisites and main curriculum
                var progressPercentage = totalResources > 0
                    ? (float)learnedResources / totalResources * 100
                    : 0f;

                // Calculate total and learned resources for advanced topics
                var totalAdvancedResources = advancedTopicsLessons.Sum(l => l.Resources.Count);
                var learnedAdvancedResources = advancedTopicsLessons.Sum(l => l.Resources.Count(r => r.Learned));

                // Calculate progress percentage for advanced topics
                var advancedTopicProgressPercentage = totalAdvancedResources > 0
                    ? (float)learnedAdvancedResources / totalAdvancedResources * 100
                    : 0f;

                // Check if the study plan is completed (i.e., all resources are learned in prerequisites and main curriculum)
                var isCompleted = learnedResources == totalResources && totalResources > 0;

                // Add the study plan result
                studyPlanResults.Add(new StudyPlanDTO
                {
                    StudyPlan = new StudyPlanDetail
                    {
                        Id = studyPlanId,
                        Title = studyPlanNode.Properties["title"].As<string>(),
                        Introduction = studyPlanNode.Properties.ContainsKey("introduction") ? new Introduction { Description = studyPlanNode.Properties["introduction"].As<string>(), Keywords = studyPlanNode.Properties.ContainsKey("Keywords") ? studyPlanNode.Properties["Keywords"].As<List<string>>() : new List<string>() } : null,
                        Prerequisite = prerequisiteLessons,
                        MainCurriculum = mainCurriculumLessons,
                        AdvancedTopics = advancedTopicsLessons, // Still return advanced topics separately
                        ProgressPercentage = progressPercentage, // Overall progress percentage (excluding advanced topics)
                        AdvancedTopicProgressPercentage = advancedTopicProgressPercentage, // Progress for advanced topics
                        Completed = isCompleted
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
        if (lessonData == null)
        {
            _logger.LogError("lessonData is null.");
            return new List<Lesson>(); // Return an empty list if lessonData is null
        }

        return lessonData
            .Select(data =>
            {
                if (data == null)
                {
                    _logger.LogError("A null entry in lessonData.");
                    return null; // Skip null entries
                }

                var lessonDict = data as Dictionary<string, object>;
                if (lessonDict == null)
                {
                    _logger.LogError("Failed to cast data to Dictionary<string, object>. Data: {data}", data);
                    return null; // Skip if casting fails
                }

                if (!lessonDict.ContainsKey("lesson") || lessonDict["lesson"] == null)
                {
                    _logger.LogError("Lesson node is missing or null in lessonDict: {lessonDict}", lessonDict);
                    return null; // Skip if lesson node is missing or null
                }

                var lessonNode = lessonDict["lesson"] as INode;
                if (lessonNode == null)
                {
                    _logger.LogError("Failed to cast 'lesson' to INode in lessonDict: {lessonDict}");
                    return null; // Skip if lessonNode is not valid
                }

                // Safely cast resources and handle potential nulls
                var resourcesRawData = lessonDict.ContainsKey("resources") ? lessonDict["resources"] as List<object> : null;
                _logger.LogInformation("lessonDict: {lessonDict}");
                _logger.LogInformation("resourcesRawData: {resourcesRawData}");

                var resources = resourcesRawData?.Select(resRaw =>
                {
                    if (resRaw == null)
                    {
                        _logger.LogError("A null entry in resourcesRawData.");
                        return null; // Skip null resource entries
                    }

                    var resDict = resRaw as Dictionary<string, object>;
                    if (resDict == null)
                    {
                        _logger.LogError("Failed to cast resource to Dictionary<string, object>. Resource: {resRaw}");
                        return null; // Skip if casting fails
                    }

                    var link = resDict.ContainsKey("resource") ? resDict["resource"]?.ToString() : null;
                    var name = resDict.ContainsKey("name") ? resDict["name"]?.ToString() : null;
                    var learned = resDict.ContainsKey("learned") && Convert.ToBoolean(resDict["learned"]);

                    // Only return valid resources
                    if (link == null && name == null)
                    {
                        return null; // Skip resource if both link and name are null
                    }

                    return new ResourceDTO
                    {
                        Link = link,
                        Name = name,
                        Learned = learned
                    };
                }).Where(r => r != null).ToList() ?? new List<ResourceDTO>(); // Return an empty list if no valid resources

                var finishedResourcesCount = resources?.Count(r => r.Learned) ?? 0;
                var totalResources = resources?.Count ?? 0;
                var progressPercentage = totalResources > 0 ? (finishedResourcesCount / (float)totalResources) * 100 : 0;

                return new Lesson
                {
                    Id = lessonNode.Properties.ContainsKey("id") ? lessonNode.Properties["id"]?.As<string>() : "No ID available",
                    Name = lessonNode.Properties.ContainsKey("name") ? lessonNode.Properties["name"]?.As<string>() : "Unnamed Lesson",
                    Description = lessonNode.Properties.ContainsKey("description") ? lessonNode.Properties["description"]?.As<string>() : "No description available",
                    Keywords = lessonNode.Properties.ContainsKey("keywords") ? lessonNode.Properties["keywords"]?.As<List<string>>() : new List<string>(),
                    Resources = resources, // Return resources or an empty list
                    FinishedResourcesCount = finishedResourcesCount,
                    ProgressPercentage = progressPercentage
                };
            })
            .Where(lesson => lesson != null) // Filter out any null lessons
            .ToList(); // Convert to List<Lesson>
    }

    public async Task<StudyPlanDTO?> GetStudyPlanByIdAsync(string studyPlanId, string targetUserId, string currentUserId)
    {
        using var session = _neo4jDriver.AsyncSession();
        try
        {
            var result = await session.RunAsync($@"
   MATCH (u:User {{id: $targetUserId}})-[:CREATED]->(sp:StudyPlan {{id: $studyPlanId}})
            WHERE sp.privacy = 'public' OR sp.privacy = 'shared' OR u.id = $currentUserId
            OPTIONAL MATCH (sp)-[:HAS_PREREQUISITE]->(pr:Lesson)
            OPTIONAL MATCH (pr)-[:HAS_RESOURCE]->(prRes:Resource)
            OPTIONAL MATCH (sp)-[:HAS_MAIN_CURRICULUM]->(mc:Lesson)
            OPTIONAL MATCH (mc)-[:HAS_RESOURCE]->(mcRes:Resource)
            OPTIONAL MATCH (sp)-[:HAS_ADVANCED_TOPIC]->(at:Lesson)
            OPTIONAL MATCH (at)-[:HAS_RESOURCE]->(atRes:Resource)
            WITH sp, pr, prRes, mc, mcRes, at, atRes,
                 EXISTS((pr)-[:FINISHED_LEARNING {{userId: u.id}}]->(prRes)) AS prLearned,
                 EXISTS((mc)-[:FINISHED_LEARNING {{userId: u.id}}]->(mcRes)) AS mcLearned,
                 EXISTS((at)-[:FINISHED_LEARNING {{userId: u.id}}]->(atRes)) AS atLearned
            WITH sp, pr, mc, at,
                 collect(DISTINCT {{resource: prRes.link, name: prRes.name, learned: prLearned}}) AS prResources,
                 collect(DISTINCT {{resource: mcRes.link, name: mcRes.name, learned: mcLearned}}) AS mcResources,
                 collect(DISTINCT {{resource: atRes.link, name: atRes.name, learned: atLearned}}) AS atResources
            RETURN sp AS StudyPlan, 
                   sp.id AS studyPlanId,
                   collect(DISTINCT {{lesson: pr, lessonId: pr.id, resources: prResources}}) AS Prerequisites, 
                   collect(DISTINCT {{lesson: mc, lessonId: mc.id, resources: mcResources}}) AS MainCurriculum,
                   collect(DISTINCT {{lesson: at, lessonId: at.id, resources: atResources}}) AS AdvancedTopics
        ", new { studyPlanId, currentUserId, targetUserId });


            var record = await result.SingleAsync();
            if (record == null || !record.Values.Any()) return null;

            var studyPlanNode = record["StudyPlan"].As<INode>();
            var prerequisitesData = record["Prerequisites"].As<List<object>>();
            var mainCurriculumData = record["MainCurriculum"].As<List<object>>();
            var advancedTopicsData = record["AdvancedTopics"].As<List<object>>();

            // Transform data into DTOs
            var prerequisites = TransformLessonsWithProgress(prerequisitesData);
            var mainCurriculum = TransformLessonsWithProgress(mainCurriculumData);
            var advancedTopics = TransformLessonsWithProgress(advancedTopicsData);

            // Calculate progress percentages
            var totalResources = prerequisites.Sum(l => l.Resources.Count) + mainCurriculum.Sum(l => l.Resources.Count);
            var learnedResources = prerequisites.Sum(l => l.Resources.Count(r => r.Learned)) + mainCurriculum.Sum(l => l.Resources.Count(r => r.Learned));
            var progressPercentage = totalResources > 0 ? (float)learnedResources / totalResources * 100 : 0;

            var totalAdvancedResources = advancedTopics.Sum(l => l.Resources.Count);
            var learnedAdvancedResources = advancedTopics.Sum(l => l.Resources.Count(r => r.Learned));
            var advancedTopicProgressPercentage = totalAdvancedResources > 0 ? (float)learnedAdvancedResources / totalAdvancedResources * 100 : 0;

            var isCompleted = learnedResources == totalResources && totalResources > 0;

            return new StudyPlanDTO
            {
                StudyPlan = new StudyPlanDetail
                {
                    Id = studyPlanNode.Properties["id"].As<string>(),
                    Title = studyPlanNode.Properties["title"].As<string>(),
                    Introduction = studyPlanNode.Properties.ContainsKey("introduction")
                        ? new Introduction
                        {
                            Description = studyPlanNode.Properties["introduction"].As<string>(),
                            Keywords = studyPlanNode.Properties.ContainsKey("keywords")
                                ? studyPlanNode.Properties["keywords"].As<List<string>>()
                                : new List<string>()
                        }
                        : null,
                    Prerequisite = prerequisites,
                    MainCurriculum = mainCurriculum,
                    AdvancedTopics = advancedTopics,
                    ProgressPercentage = progressPercentage,
                    AdvancedTopicProgressPercentage = advancedTopicProgressPercentage,
                    Completed = isCompleted
                }
            };
        }
        finally
        {
            await session.CloseAsync();
        }
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
                OPTIONAL MATCH (p)-[:HAS_PREREQUISITE|HAS_MAIN_CURRICULUM|HAS_ADVANCED_TOPIC]->(l:Lesson)
                DETACH DELETE p, l
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

    public async Task<bool> MarkStudyPlanAsCompletedAsync(string studyPlanTitle, string userId)
    {
        var session = _neo4jDriver.AsyncSession();
        try
        {
            var result = await session.RunAsync($@"
            MATCH (u:User {{id: $userId}})-[r:CREATED]->(p:StudyPlan {{title: $title}})
            CREATE (u)-[rel:COMPLETED]->(p)
            RETURN COUNT(rel) > 0",
                new { userId, title = studyPlanTitle });

            var summary = await result.ConsumeAsync();
            return summary.Counters.RelationshipsCreated > 0;
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    public async Task<bool> DisMarkStudyPlanAsCompletedAsync(string studyPlanTitle, string userId)
    {
        var session = _neo4jDriver.AsyncSession();
        try
        {
            var result = await session.RunAsync($@"
            MATCH (u:User {{id: $userId}})-[r:COMPLETED]->(p:StudyPlan {{title: $title}})
            DELETE r
            RETURN COUNT(r) > 0",
                new { userId, title = studyPlanTitle });

            var summary = await result.ConsumeAsync();
            return summary.Counters.RelationshipsDeleted > 0;
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    public async Task<int> CountCompletedStudyPlansAsync(string userId)
    {
        var session = _neo4jDriver.AsyncSession();
        try
        {
            var result = await session.RunAsync($@"
            MATCH (u:User {{id: $userId}})-[r:COMPLETED]->(p:StudyPlan)
            RETURN COUNT(r)",
                new { userId });

            var count = await result.SingleAsync();
            return count[0].As<int>();
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    public async Task<bool> SetStudyPlanPrivacyAsync(string userId, string studyPlanId, string privacy)
    {
        using var session = _neo4jDriver.AsyncSession();
        try
        {
            var result = await session.ExecuteWriteAsync(async transaction =>
            {
                var cypherQuery = $@"
                    MATCH (user:User {{id: $userId}})-[:HAS_PLAN]->(studyplan:StudyPlan {{id: $studyPlanId}})
                    SET studyplan.privacy = $privacy
                    RETURN studyplan";

                var response = await transaction.RunAsync(cypherQuery, new
                {
                    userId = userId,
                    studyPlanId = studyPlanId,
                    privacy = privacy
                });

                return await response.SingleAsync();
            });

            return result != null;  // Return true if the study plan was found and updated
        }
        catch (Exception ex)
        {
            // Log the exception (you can inject a logger if needed)
            Console.WriteLine($"Error updating study plan privacy: {ex.Message}");
            return false;
        }
        finally
        {
            await session.CloseAsync();
        }
    }
}
