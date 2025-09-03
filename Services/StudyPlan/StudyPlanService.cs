using Neo4j.Driver;
using System.Linq;
using Sciencetopia.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

public class StudyPlanService
{
    private readonly IDriver _neo4jDriver;
    private readonly ILogger<StudyPlanService> _logger;
    private readonly IStudyPlanRepository _sqlRepository;

    public StudyPlanService(IDriver neo4jDriver, ILogger<StudyPlanService> logger, IStudyPlanRepository sqlRepository)
    {
        _neo4jDriver = neo4jDriver;
        _logger = logger;
        _sqlRepository = sqlRepository;
    }

    public async Task<bool> SaveStudyPlanAsync(StudyPlanDTO studyPlanDTO, string userId)
    {
        var session = _neo4jDriver.AsyncSession();
        try
        {
            var studyPlan = studyPlanDTO.StudyPlan;
            var studyPlanId = Guid.NewGuid().ToString();

            // Step 1: Insert StudyPlan into SQL
            await _sqlRepository.InsertStudyPlanAsync(new StudyPlanEntity
            {
                Id = Guid.Parse(studyPlanId),
                Title = studyPlan.Title,
                Description = studyPlan.Introduction?.Description,
                CreatorId = Guid.Parse(userId)
            });

            // Step 2: Insert Lessons and Resources into SQL
            var allLessons = studyPlan.Prerequisite.Concat(studyPlan.MainCurriculum).Concat(studyPlan.AdvancedTopics).ToList();
            foreach (var lesson in allLessons)
            {
                var lessonId = lesson.Id ?? Guid.NewGuid().ToString();
                lesson.Id = lessonId;

                await _sqlRepository.InsertLessonAsync(new LessonEntity
                {
                    Id = Guid.Parse(lessonId),
                    Title = lesson.Name,
                    Description = lesson.Description,
                });

                foreach (var resource in lesson.Resources)
                {
                    var resourceId = resource.Id ?? Guid.NewGuid().ToString();
                    resource.Id = resourceId;

                    await _sqlRepository.InsertResourceAsync(new Resource
                    {
                        Id = Guid.Parse(resourceId),
                        Name = resource.Name,
                        Link = resource.Link
                    });
                }
            }

            // Step 3: Insert into Neo4j
            await session.ExecuteWriteAsync(async transaction =>
            {
                await transaction.RunAsync("CREATE (p:StudyPlan {id: $id})", new { id = studyPlanId });

                int orderCounter = 0;
                foreach (var (lesson, type) in GetLessonsWithType(studyPlan))
                {
                    orderCounter++;

                    await transaction.RunAsync(@"
                    MATCH (p:StudyPlan {id: $studyPlanId})
                    MERGE (l:Lesson {id: $lessonId})
                    MERGE (p)-[:HAS_STEP {type: $type, order: $order}]->(l)",
                        new { studyPlanId, lessonId = lesson.Id, type, order = orderCounter });

                    foreach (var resource in lesson.Resources)
                    {
                        await transaction.RunAsync(@"
                        MATCH (l:Lesson {id: $lessonId})
                        MERGE (r:Resource {id: $resourceId})
                        MERGE (l)-[:HAS_RESOURCE]->(r)",
                            new { lessonId = lesson.Id, resourceId = resource.Id });
                    }

                    // Associate Lesson with KnowledgeNodes
                    if (lesson.AssociatedKnowledgeNodes != null)
                    {
                        foreach (var knowledgeNode in lesson.AssociatedKnowledgeNodes)
                        {
                            await transaction.RunAsync(@"
                            MATCH (l:Lesson {id: $lessonId}), (k:KnowledgeNode {id: $knowledgeNodeId})
                            MERGE (l)-[:ASSOCIATED_WITH]->(k)",
                                new { lessonId = lesson.Id, knowledgeNodeId = knowledgeNode.Properties?.Link });
                        }
                    }
                }

                // Associate StudyPlan Introduction with KnowledgeNodes
                if (studyPlan.Introduction?.AssociatedKnowledgeNodes != null)
                {
                    foreach (var knowledgeNode in studyPlan.Introduction.AssociatedKnowledgeNodes)
                    {
                        await transaction.RunAsync(@"
                        MATCH (p:StudyPlan {id: $studyPlanId}), (k:KnowledgeNode {id: $knowledgeNodeId})
                        MERGE (p)-[:ASSOCIATED_WITH]->(k)",
                            new { studyPlanId, knowledgeNodeId = knowledgeNode.Properties?.Link });
                    }
                }

                await transaction.RunAsync(@"
                MATCH (u:User {id: $userId}), (p:StudyPlan {id: $studyPlanId})
                MERGE (u)-[:CREATED]->(p)",
                    new { userId, studyPlanId });
            });

            // Step 4: Create initial version snapshot and versioned subgraph in Neo4j
            try
            {
                var snapshot = SerializePlan(studyPlanDTO);
                var versionNo = await _sqlRepository.CreateStudyPlanVersionAsync(Guid.Parse(studyPlanId), studyPlan?.Title, studyPlan?.Introduction?.Description, snapshot, "Initial create", userId);
                await WriteVersionSubgraphAsync(session, Guid.Parse(studyPlanId), studyPlanDTO, versionNo);
            }
            catch { /* non-blocking */ }

            return true;
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    private IEnumerable<(Lesson lesson, string type)> GetLessonsWithType(StudyPlanDetail studyPlan)
    {
        foreach (var lesson in studyPlan.Prerequisite)
            yield return (lesson, "PREREQUISITE");
        foreach (var lesson in studyPlan.MainCurriculum)
            yield return (lesson, "MAIN_CURRICULUM");
        foreach (var lesson in studyPlan.AdvancedTopics)
            yield return (lesson, "ADVANCED_TOPIC");
    }

    public async Task<bool> UpdateStudyPlanAsync(StudyPlanDTO updatedStudyPlan)
    {
        using var session = _neo4jDriver.AsyncSession();
        try
        {
            var studyPlanId = updatedStudyPlan.StudyPlan.Id;

            // Step 1: 确认 StudyPlan 存在
            var existingPlanCheck = await session.ExecuteReadAsync(async transaction =>
            {
                var result = await transaction.RunAsync($@"
                MATCH (p:StudyPlan {{id: $studyPlanId}})
                RETURN p", new { studyPlanId });

                var recordList = await result.ToListAsync();
                return recordList.Any();
            });

            if (!existingPlanCheck)
            {
                _logger.LogError("StudyPlan with ID {studyPlanId} does not exist.", studyPlanId);
                return false;
            }

            var studyPlan = updatedStudyPlan.StudyPlan;

            // New behavior: create a draft and publish it (backwards compatible)
            try
            {
                var snapshot = SerializePlan(updatedStudyPlan);
                // create draft
                var draftNumber = await _sqlRepository.CreateStudyPlanDraftAsync(Guid.Parse(studyPlanId), studyPlan?.Title, studyPlan?.Introduction?.Description, snapshot, "Auto-publish via UpdateStudyPlan", null);
                // publish draft
                var published = await PublishStudyPlanDraftInternal(session, Guid.Parse(studyPlanId), draftNumber, "Auto-publish via UpdateStudyPlan", null);
                if (published)
                {
                    // create version record
                    await _sqlRepository.CreateStudyPlanVersionAsync(Guid.Parse(studyPlanId), studyPlan?.Title, studyPlan?.Introduction?.Description, snapshot, "Auto-publish via UpdateStudyPlan", null);
                    await _sqlRepository.MarkStudyPlanDraftStatusAsync(Guid.Parse(studyPlanId), draftNumber, DraftStatus.Approved, null);
                }
                return published;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to draft+publish update; fallback to direct update.");
            }

            // Step 2: 更新 SQL 中的 StudyPlan 基本信息
            await _sqlRepository.UpdateStudyPlanAsync(new StudyPlanEntity
            {
                Id = Guid.Parse(studyPlanId),
                Title = studyPlan.Title,
                Description = studyPlan.Introduction?.Description,
                UpdatedDate = DateTime.UtcNow
            });

            // Step 3: 更新 SQL 中 Lessons 基本信息
            var allLessons = studyPlan.Prerequisite.Concat(studyPlan.MainCurriculum).Concat(studyPlan.AdvancedTopics).ToList();
            foreach (var lesson in allLessons)
            {
                if (!string.IsNullOrEmpty(lesson.Id))
                {
                    await _sqlRepository.UpdateLessonAsync(new LessonEntity
                    {
                        Id = Guid.Parse(lesson.Id),
                        Title = lesson.Name,
                        Description = lesson.Description,
                        UpdatedDate = DateTime.UtcNow
                    });
                }
            }

            // Step 4: 更新 Neo4j 中 StudyPlan 和 Lesson 的关系
            await session.ExecuteWriteAsync(async transaction =>
            {
                // 删除旧 HAS_STEP
                await transaction.RunAsync(@"
                MATCH (p:StudyPlan {id: $studyPlanId})-[r:HAS_STEP]->(l:Lesson)
                DELETE r", new { studyPlanId });

                int orderCounter = 0;
                foreach (var (lesson, type) in GetLessonsWithType(studyPlan))
                {
                    orderCounter++;

                    // Merge Lesson节点，仅根据id
                    await transaction.RunAsync(@"
                    MERGE (l:Lesson {id: $lessonId})",
                        new { lessonId = lesson.Id });

                    // 创建 HAS_STEP 关系
                    await transaction.RunAsync(@"
                    MATCH (p:StudyPlan {id: $studyPlanId}), (l:Lesson {id: $lessonId})
                    MERGE (p)-[:HAS_STEP {type: $type, order: $order}]->(l)",
                        new { studyPlanId, lessonId = lesson.Id, type, order = orderCounter });

                    // 删除旧 ASSOCIATED_WITH（Lesson）
                    await transaction.RunAsync(@"
                    MATCH (l:Lesson {id: $lessonId})-[r:ASSOCIATED_WITH]->(k:KnowledgeNode)
                    DELETE r",
                        new { lessonId = lesson.Id });

                    // 重建新的 ASSOCIATED_WITH（Lesson）
                    if (lesson.AssociatedKnowledgeNodes != null)
                    {
                        foreach (var knowledgeNode in lesson.AssociatedKnowledgeNodes)
                        {
                            await transaction.RunAsync(@"
                            MATCH (l:Lesson {id: $lessonId}), (k:KnowledgeNode {id: $knowledgeNodeId})
                            MERGE (l)-[:ASSOCIATED_WITH]->(k)",
                                new { lessonId = lesson.Id, knowledgeNodeId = knowledgeNode.Properties?.Link });
                        }
                    }

                    // 更新 Lesson 的 Resource 关系
                    foreach (var resource in lesson.Resources)
                    {
                        if (!string.IsNullOrEmpty(resource.Name) && !string.IsNullOrEmpty(resource.Link))
                        {
                            await transaction.RunAsync(@"
                            MERGE (r:Resource {id: $resourceId})
                            ON CREATE SET r.name = $resourceName, r.link = $resourceLink
                            ON MATCH SET r.name = $resourceName, r.link = $resourceLink
                            WITH r
                            MATCH (l:Lesson {id: $lessonId})
                            MERGE (l)-[:HAS_RESOURCE]->(r)",
                                new { lessonId = lesson.Id, resourceId = resource.Id, resourceName = resource.Name, resourceLink = resource.Link });
                        }
                    }
                }

                // 删除旧 ASSOCIATED_WITH（StudyPlan）
                await transaction.RunAsync(@"
                MATCH (p:StudyPlan {id: $studyPlanId})-[r:ASSOCIATED_WITH]->(k:KnowledgeNode)
                DELETE r", new { studyPlanId });

                // 重建新的 ASSOCIATED_WITH（StudyPlan）
                if (studyPlan.Introduction?.AssociatedKnowledgeNodes != null)
                {
                    foreach (var knowledgeNode in studyPlan.Introduction.AssociatedKnowledgeNodes)
                    {
                        await transaction.RunAsync(@"
                        MATCH (p:StudyPlan {id: $studyPlanId}), (k:KnowledgeNode {id: $knowledgeNodeId})
                        MERGE (p)-[:ASSOCIATED_WITH]->(k)",
                            new { studyPlanId, knowledgeNodeId = knowledgeNode.Properties?.Link });
                    }
                }
            });

            return true;
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

    private string SerializePlan(StudyPlanDTO dto)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false };
        return JsonSerializer.Serialize(dto, options);
    }

    private StudyPlanDTO? DeserializePlan(string json)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return JsonSerializer.Deserialize<StudyPlanDTO>(json, options);
    }

    // Save a draft (does not alter live data/graph)
    public async Task<(bool ok, int draftNumber)> SaveStudyPlanDraftAsync(StudyPlanDTO dto, string userId, string? changeNotes)
    {
        if (dto?.StudyPlan?.Id == null) return (false, 0);
        var planId = Guid.Parse(dto.StudyPlan.Id);
        var snapshot = SerializePlan(dto);
        var draftNo = await _sqlRepository.CreateStudyPlanDraftAsync(planId, dto.StudyPlan.Title, dto.StudyPlan.Introduction?.Description, snapshot, changeNotes, userId);
        return (true, draftNo);
    }

    // Publish a draft: create version and apply to SQL + Neo4j
    public async Task<(bool ok, int versionNumber)> PublishStudyPlanDraftAsync(string studyPlanId, int draftNumber, string userId, string? changeNotes)
    {
        using var session = _neo4jDriver.AsyncSession();
        try
        {
            var planGuid = Guid.Parse(studyPlanId);
            // Load draft and create version first to get versionNumber
            var draft = await _sqlRepository.GetStudyPlanDraftAsync(planGuid, draftNumber);
            if (draft == null) return (false, 0);
            var versionNo = await _sqlRepository.CreateStudyPlanVersionAsync(planGuid, draft.Title, draft.Description, draft.SnapshotJson, changeNotes, userId);

            // Build versioned subgraph in Neo4j for historical queries
            var dto = DeserializePlan(draft.SnapshotJson);
            if (dto?.StudyPlan == null) return (false, 0);
            await WriteVersionSubgraphAsync(session, planGuid, dto, versionNo);

            // Apply to live graph
            var success = await PublishStudyPlanDraftInternal(session, planGuid, draftNumber, changeNotes, userId);
            if (!success) return (false, 0);

            await _sqlRepository.MarkStudyPlanDraftStatusAsync(planGuid, draftNumber, DraftStatus.Approved, userId);
            return (true, versionNo);
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    private async Task WriteVersionSubgraphAsync(IAsyncSession session, Guid studyPlanId, StudyPlanDTO dto, int versionNumber)
    {
        var spId = studyPlanId.ToString();
        await session.ExecuteWriteAsync(async tx =>
        {
            // Version anchor node
            await tx.RunAsync(@"
                MATCH (sp:StudyPlan {id: $studyPlanId})
                MERGE (pv:PlanVersion {studyPlanId: $studyPlanId, versionNumber: $versionNumber})
                MERGE (sp)-[:HAS_VERSION]->(pv)
            ", new { studyPlanId = spId, versionNumber });

            int orderCounter = 0;
            foreach (var (lesson, type) in GetLessonsWithType(dto.StudyPlan!))
            {
                orderCounter++;
                await tx.RunAsync(@"MERGE (l:Lesson {id: $lessonId})",
                    new { lessonId = lesson.Id });
                await tx.RunAsync(@"
                    MATCH (pv:PlanVersion {studyPlanId: $studyPlanId, versionNumber: $versionNumber}), (l:Lesson {id: $lessonId})
                    MERGE (pv)-[:HAS_STEP {type: $type, order: $order}]->(l)
                ", new { studyPlanId = spId, versionNumber, lessonId = lesson.Id, type, order = orderCounter });

                foreach (var resource in lesson.Resources ?? new List<ResourceDTO>())
                {
                    if (!string.IsNullOrEmpty(resource.Id))
                    {
                        await tx.RunAsync(@"MERGE (r:Resource {id: $resourceId})",
                            new { resourceId = resource.Id });
                        await tx.RunAsync(@"
                            MATCH (pv:PlanVersion {studyPlanId: $studyPlanId, versionNumber: $versionNumber}), (r:Resource {id: $resourceId})
                            MERGE (pv)-[:HAS_RESOURCE {lessonId: $lessonId}]->(r)
                        ", new { studyPlanId = spId, versionNumber, resourceId = resource.Id, lessonId = lesson.Id });
                    }
                }
            }

            // Study plan knowledge node associations per version
            if (dto.StudyPlan!.Introduction?.AssociatedKnowledgeNodes != null)
            {
                foreach (var knowledgeNode in dto.StudyPlan.Introduction.AssociatedKnowledgeNodes)
                {
                    await tx.RunAsync(@"
                        MATCH (pv:PlanVersion {studyPlanId: $studyPlanId, versionNumber: $versionNumber}), (k:KnowledgeNode {id: $knowledgeNodeId})
                        MERGE (pv)-[:ASSOCIATED_WITH]->(k)
                    ", new { studyPlanId = spId, versionNumber, knowledgeNodeId = knowledgeNode.Properties?.Link });
                }
            }
        });
    }

    private async Task<bool> PublishStudyPlanDraftInternal(IAsyncSession session, Guid studyPlanId, int draftNumber, string? changeNotes, string? userId)
    {
        var draft = await _sqlRepository.GetStudyPlanDraftAsync(studyPlanId, draftNumber);
        if (draft == null) return false;
        var dto = DeserializePlan(draft.SnapshotJson);
        if (dto?.StudyPlan == null) return false;

        // 1) Update SQL title/description
        await _sqlRepository.UpdateStudyPlanAsync(new StudyPlanEntity
        {
            Id = studyPlanId,
            Title = dto.StudyPlan.Title ?? string.Empty,
            Description = dto.StudyPlan.Introduction?.Description,
            UpdatedDate = DateTime.UtcNow
        });

        // 2) Update Lessons basic info in SQL (existing IDs only)
        var allLessons = (dto.StudyPlan.Prerequisite ?? new List<Lesson>())
            .Concat(dto.StudyPlan.MainCurriculum ?? new List<Lesson>())
            .Concat(dto.StudyPlan.AdvancedTopics ?? new List<Lesson>())
            .ToList();

        foreach (var lesson in allLessons)
        {
            if (!string.IsNullOrEmpty(lesson.Id))
            {
                await _sqlRepository.UpdateLessonAsync(new LessonEntity
                {
                    Id = Guid.Parse(lesson.Id),
                    Title = lesson.Name ?? string.Empty,
                    Description = lesson.Description,
                    UpdatedDate = DateTime.UtcNow
                });
            }
        }

        // 3) Update Neo4j relationships to match snapshot
        await session.ExecuteWriteAsync(async transaction =>
        {
            var studyPlanIdStr = studyPlanId.ToString();
            await transaction.RunAsync(@"MATCH (p:StudyPlan {id: $studyPlanId})-[r:HAS_STEP]->(l:Lesson) DELETE r", new { studyPlanId = studyPlanIdStr });

            int orderCounter = 0;
            foreach (var (lesson, type) in GetLessonsWithType(dto.StudyPlan))
            {
                orderCounter++;
                await transaction.RunAsync(@"MERGE (l:Lesson {id: $lessonId})", new { lessonId = lesson.Id });
                await transaction.RunAsync(@"MATCH (p:StudyPlan {id: $studyPlanId}), (l:Lesson {id: $lessonId}) MERGE (p)-[:HAS_STEP {type: $type, order: $order}]->(l)", new { studyPlanId = studyPlanIdStr, lessonId = lesson.Id, type, order = orderCounter });

                await transaction.RunAsync(@"MATCH (l:Lesson {id: $lessonId})-[r:ASSOCIATED_WITH]->(k:KnowledgeNode) DELETE r", new { lessonId = lesson.Id });
                if (lesson.AssociatedKnowledgeNodes != null)
                {
                    foreach (var knowledgeNode in lesson.AssociatedKnowledgeNodes)
                    {
                        await transaction.RunAsync(@"MATCH (l:Lesson {id: $lessonId}), (k:KnowledgeNode {id: $knowledgeNodeId}) MERGE (l)-[:ASSOCIATED_WITH]->(k)", new { lessonId = lesson.Id, knowledgeNodeId = knowledgeNode.Properties?.Link });
                    }
                }

                foreach (var resource in lesson.Resources ?? new List<ResourceDTO>())
                {
                    if (!string.IsNullOrEmpty(resource.Id))
                    {
                        await transaction.RunAsync(@"MERGE (r:Resource {id: $resourceId}) WITH r MATCH (l:Lesson {id: $lessonId}) MERGE (l)-[:HAS_RESOURCE]->(r)", new { lessonId = lesson.Id, resourceId = resource.Id });
                    }
                }
            }

            await transaction.RunAsync(@"MATCH (p:StudyPlan {id: $studyPlanId})-[r:ASSOCIATED_WITH]->(k:KnowledgeNode) DELETE r", new { studyPlanId = studyPlanIdStr });
            if (dto.StudyPlan.Introduction?.AssociatedKnowledgeNodes != null)
            {
                foreach (var knowledgeNode in dto.StudyPlan.Introduction.AssociatedKnowledgeNodes)
                {
                    await transaction.RunAsync(@"MATCH (p:StudyPlan {id: $studyPlanId}), (k:KnowledgeNode {id: $knowledgeNodeId}) MERGE (p)-[:ASSOCIATED_WITH]->(k)", new { studyPlanId = studyPlanIdStr, knowledgeNodeId = knowledgeNode.Properties?.Link });
                }
            }
        });

        return true;
    }

    public async Task<List<StudyPlanDTO>> GetStudyPlansByUserIdAsync(string currentUserId, string targetUserId)
    {
        using var session = _neo4jDriver.AsyncSession();
        try
        {
            // 1. 从Neo4j拉取用户加入(ENROLLED_IN)的PlanVersion，并取其studyPlanId
            var studyPlanIds = new List<string>();
            var enrolledQuery = @"MATCH (u:User {id: $targetUserId})-[:ENROLLED_IN]->(pv:PlanVersion) RETURN pv.studyPlanId AS studyPlanId";
            var enrolledResult = await session.RunAsync(enrolledQuery, new { targetUserId });
            await foreach (var rec in enrolledResult)
            {
                var pid = rec["studyPlanId"].As<string>();
                if (!string.IsNullOrEmpty(pid)) studyPlanIds.Add(pid);
            }

            // 拉取SQL中的StudyPlan基本信息（按Id集合）
            var studyPlans = new List<StudyPlanEntity>();
            foreach (var pid in studyPlanIds.Distinct())
            {
                var sp = await _sqlRepository.GetStudyPlanByIdAsync(pid);
                if (sp != null) studyPlans.Add(sp);
            }

            // 2. 从Neo4j拉取所有Lesson关系
            var lessonInfo = await GetLessonIdsByStudyPlanIdsAsync(studyPlanIds);

            // 3. 拿到所有LessonId
            var lessonIds = lessonInfo.Select(x => x.LessonId).Distinct().ToList();

            // 4. 从SQL拉取Lessons
            var lessons = await GetLessonsByIdsAsync(lessonIds);

            // 5. 从Neo4j拉取Lesson->Resource关系
            var resourceInfo = await GetResourceIdsByLessonIdsAsync(lessonIds);

            // 6. 拿到所有ResourceId
            var resourceIds = resourceInfo.SelectMany(x => x.Value).Distinct().ToList();

            // 7. 从SQL拉取Resources
            var resources = await GetResourcesByIdsAsync(resourceIds);

            // Step 4. 从Neo4j拉取结构关系（HAS_STEP, HAS_RESOURCE, ASSOCIATED_WITH）
            var cypherQuery = @"
            MATCH (sp:StudyPlan)
            WHERE sp.id IN $studyPlanIds
            OPTIONAL MATCH (sp)-[hs:HAS_STEP]->(l:Lesson)
            OPTIONAL MATCH (l)-[:HAS_RESOURCE]->(r:Resource)
            OPTIONAL MATCH (cu:User {id: $currentUserId})-[cr:COMPLETED]->(r)
            OPTIONAL MATCH (l)-[:ASSOCIATED_WITH]->(lk:KnowledgeNode)
            OPTIONAL MATCH (sp)-[:ASSOCIATED_WITH]->(sk:KnowledgeNode)
            RETURN sp.id AS studyPlanId, 
                   l.id AS lessonId, 
                   r.id AS resourceId, 
                   hs.type AS stepType,
                   hs.order AS stepOrder,
                   (cr IS NOT NULL) AS learned,
                   collect(DISTINCT lk) AS lessonKnowledgeNodes,
                   collect(DISTINCT sk) AS studyPlanKnowledgeNodes
            ORDER BY hs.order";

            var result = await session.RunAsync(cypherQuery, new { currentUserId, studyPlanIds });

            // Step 5. 整合数据
            var studyPlanDict = new Dictionary<string, StudyPlanDTO>();

            await foreach (var record in result)
            {
                var studyPlanId = record["studyPlanId"]?.As<string>();
                if (string.IsNullOrEmpty(studyPlanId)) continue;

                if (!studyPlanDict.ContainsKey(studyPlanId))
                {
                    var sqlStudyPlan = studyPlans.FirstOrDefault(sp => sp.Id.ToString() == studyPlanId);
                    if (sqlStudyPlan == null) continue; // 数据不一致保护

                    studyPlanDict[studyPlanId] = new StudyPlanDTO
                    {
                        StudyPlan = new StudyPlanDetail
                        {
                            Id = studyPlanId,
                            Title = sqlStudyPlan.Title,
                            Introduction = new Introduction
                            {
                                Description = sqlStudyPlan.Description,
                                AssociatedKnowledgeNodes = record["studyPlanKnowledgeNodes"]
                                    ?.As<List<INode>>()?.Select(MapNode).ToList() ?? new List<Node>()
                            },
                            Prerequisite = new List<Lesson>(),
                            MainCurriculum = new List<Lesson>(),
                            AdvancedTopics = new List<Lesson>()
                        }
                    };
                }

                var lessonId = record["lessonId"]?.As<string>();
                if (string.IsNullOrEmpty(lessonId)) continue;

                var sqlLesson = lessons.FirstOrDefault(l => l.Value.Id.ToString() == lessonId).Value;
                if (sqlLesson == null) continue;

                var lesson = new Lesson
                {
                    Id = lessonId,
                    Name = sqlLesson.Title,
                    Description = sqlLesson.Description,
                    Resources = new List<ResourceDTO>(),
                    AssociatedKnowledgeNodes = record["lessonKnowledgeNodes"]
                        ?.As<List<INode>>()?.Select(MapNode).ToList() ?? new List<Node>()
                };

                var resourceId = record["resourceId"]?.As<string>();
                if (!string.IsNullOrEmpty(resourceId))
                {
                    var sqlResource = resources.FirstOrDefault(r => r.Value.Id.ToString() == resourceId).Value;
                    if (sqlResource != null)
                    {
                        var learned = false;
                        try { learned = record["learned"].As<bool>(); } catch {}
                        lesson.Resources.Add(new ResourceDTO
                        {
                            Id = sqlResource.Id.ToString(),
                            Name = sqlResource.Name,
                            Link = sqlResource.Link,
                            Learned = learned
                        });
                    }
                }

                var stepType = record["stepType"]?.As<string>();
                if (stepType == "PREREQUISITE")
                    studyPlanDict[studyPlanId].StudyPlan.Prerequisite.Add(lesson);
                else if (stepType == "MAIN_CURRICULUM")
                    studyPlanDict[studyPlanId].StudyPlan.MainCurriculum.Add(lesson);
                else if (stepType == "ADVANCED_TOPIC")
                    studyPlanDict[studyPlanId].StudyPlan.AdvancedTopics.Add(lesson);
            }

            // Step 6. 最后计算学习进度
            foreach (var studyPlan in studyPlanDict.Values)
            {
                CalculateProgress(studyPlan.StudyPlan);
            }

            return studyPlanDict.Values.ToList();
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    // 小工具函数，把Neo4j Node转为你的Node结构
    private Node MapNode(INode n)
    {
        return new Node
        {
            Identity = (int)n.Id,
            Labels = n.Labels.ToList(),
            Properties = new NodeProperties
            {
                Link = n.Properties.ContainsKey("link") ? n.Properties["link"]?.ToString() : null,
                Name = n.Properties.ContainsKey("name") ? n.Properties["name"]?.ToString() : null
            },
            ElementId = n.ElementId
        };
    }

    // 小工具函数，计算Progress
    private void CalculateProgress(StudyPlanDetail studyPlan)
    {
        var allLessons = studyPlan.Prerequisite.Concat(studyPlan.MainCurriculum).ToList();
        var totalResources = allLessons.Sum(l => l.Resources.Count);
        var learnedResources = allLessons.Sum(l => l.Resources.Count(r => r.Learned));

        studyPlan.ProgressPercentage = totalResources > 0
            ? (float)learnedResources / totalResources * 100
            : 0;

        var advancedLessons = studyPlan.AdvancedTopics;
        var totalAdvResources = advancedLessons.Sum(l => l.Resources.Count);
        var learnedAdvResources = advancedLessons.Sum(l => l.Resources.Count(r => r.Learned));

        studyPlan.AdvancedTopicProgressPercentage = totalAdvResources > 0
            ? (float)learnedAdvResources / totalAdvResources * 100
            : 0;

        studyPlan.Completed = totalResources > 0 && learnedResources == totalResources;
    }

    private async Task<List<(string StudyPlanId, string LessonId, string Type, int Order)>> GetLessonIdsByStudyPlanIdsAsync(List<string> studyPlanIds)
    {
        var session = _neo4jDriver.AsyncSession();
        try
        {
            var result = await session.RunAsync(@"
            MATCH (sp:StudyPlan)-[hs:HAS_STEP]->(l:Lesson)
            WHERE sp.id IN $studyPlanIds
            RETURN sp.id AS studyPlanId, l.id AS lessonId, hs.type AS stepType, hs.order AS stepOrder
            ORDER BY sp.id, hs.order
        ", new { studyPlanIds });

            var lessonInfos = new List<(string StudyPlanId, string LessonId, string Type, int Order)>();

            await foreach (var record in result)
            {
                var studyPlanId = record["studyPlanId"].As<string>();
                var lessonId = record["lessonId"].As<string>();
                var stepType = record["stepType"].As<string>();
                var stepOrder = record["stepOrder"].As<int>();

                lessonInfos.Add((studyPlanId, lessonId, stepType, stepOrder));
            }

            return lessonInfos;
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    private async Task<List<(string LessonId, string Type, int Order)>> GetLessonIdsByStudyPlanIdAsync(string studyPlanId)
    {
        var session = _neo4jDriver.AsyncSession();
        try
        {
            var result = await session.RunAsync(@"
            MATCH (sp:StudyPlan {id: $studyPlanId})-[hs:HAS_STEP]->(l:Lesson)
            RETURN l.id AS lessonId, hs.type AS stepType, hs.order AS stepOrder
            ORDER BY hs.order
        ", new { studyPlanId });

            var lessonInfos = new List<(string LessonId, string Type, int Order)>();

            await foreach (var record in result)
            {
                var lessonId = record["lessonId"].As<string>();
                var stepType = record["stepType"].As<string>();
                var stepOrder = record["stepOrder"].As<int>();

                lessonInfos.Add((lessonId, stepType, stepOrder));
            }

            return lessonInfos;
        }
        finally
        {
            await session.CloseAsync();
        }
    }


    private async Task<Dictionary<string, LessonEntity>> GetLessonsByIdsAsync(List<string> lessonIds)
    {
        if (lessonIds == null || lessonIds.Count == 0)
            return new Dictionary<string, LessonEntity>();

        var guidIds = lessonIds
            .Select(id => Guid.TryParse(id, out var guid) ? guid : (Guid?)null)
            .Where(guid => guid.HasValue)
            .Select(guid => guid.Value)
            .ToList();

        var lessons = await _sqlRepository.GetLessonsByIdsAsync(guidIds.Select(g => g.ToString()).ToList());

        return lessons.ToDictionary(l => l.Id.ToString(), l => l);
    }

    private async Task<Dictionary<string, List<string>>> GetResourceIdsByLessonIdsAsync(List<string> lessonIds)
    {
        var session = _neo4jDriver.AsyncSession();
        try
        {
            var result = await session.RunAsync(@"
            MATCH (l:Lesson)
            WHERE l.id IN $lessonIds
            MATCH (l)-[:HAS_RESOURCE]->(r:Resource)
            RETURN l.id AS lessonId, collect(r.id) AS resourceIds
        ", new { lessonIds });

            var lessonResourceMap = new Dictionary<string, List<string>>();

            await foreach (var record in result)
            {
                var lessonId = record["lessonId"].As<string>();
                var resourceIds = record["resourceIds"].As<List<object>>()
                    .Select(id => id.ToString())
                    .ToList();

                lessonResourceMap[lessonId] = resourceIds;
            }

            return lessonResourceMap;
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    private async Task<Dictionary<string, Resource>> GetResourcesByIdsAsync(List<string> resourceIds)
    {
        if (resourceIds == null || resourceIds.Count == 0)
            return new Dictionary<string, Resource>();

        var guidIds = resourceIds
            .Select(id => Guid.TryParse(id, out var guid) ? guid : (Guid?)null)
            .Where(guid => guid.HasValue)
            .Select(guid => guid.Value)
            .ToList();

        var resources = await _sqlRepository.GetResourcesByIdsAsync(guidIds.Select(g => g.ToString()).ToList());

        return resources.ToDictionary(r => r.Id.ToString(), r => r);
    }

    // private List<Lesson> TransformLessonsWithProgress(List<object> lessonData)
    // {
    //     if (lessonData == null)
    //     {
    //         _logger.LogError("lessonData is null.");
    //         return new List<Lesson>(); // Return an empty list if lessonData is null
    //     }

    //     return lessonData
    //         .Select(data =>
    //         {
    //             if (data == null)
    //             {
    //                 _logger.LogError("A null entry in lessonData.");
    //                 return null; // Skip null entries
    //             }

    //             var lessonDict = data as Dictionary<string, object>;
    //             if (lessonDict == null)
    //             {
    //                 _logger.LogError("Failed to cast data to Dictionary<string, object>. Data: {data}", data);
    //                 return null; // Skip if casting fails
    //             }

    //             if (!lessonDict.ContainsKey("lesson") || lessonDict["lesson"] == null)
    //             {
    //                 _logger.LogError("Lesson node is missing or null in lessonDict: {lessonDict}", lessonDict);
    //                 return null; // Skip if lesson node is missing or null
    //             }

    //             var lessonNode = lessonDict["lesson"] as INode;
    //             if (lessonNode == null)
    //             {
    //                 _logger.LogError("Failed to cast 'lesson' to INode in lessonDict: {lessonDict}");
    //                 return null; // Skip if lessonNode is not valid
    //             }

    //             // Safely cast resources and handle potential nulls
    //             var resourcesRawData = lessonDict.ContainsKey("resources") ? lessonDict["resources"] as List<object> : null;
    //             _logger.LogInformation("lessonDict: {lessonDict}");
    //             _logger.LogInformation("resourcesRawData: {resourcesRawData}");

    //             var resources = resourcesRawData?.Select(resRaw =>
    //             {
    //                 if (resRaw == null)
    //                 {
    //                     _logger.LogError("A null entry in resourcesRawData.");
    //                     return null; // Skip null resource entries
    //                 }

    //                 var resDict = resRaw as Dictionary<string, object>;
    //                 if (resDict == null)
    //                 {
    //                     _logger.LogError("Failed to cast resource to Dictionary<string, object>. Resource: {resRaw}");
    //                     return null; // Skip if casting fails
    //                 }

    //                 var link = resDict.ContainsKey("resource") ? resDict["resource"]?.ToString() : null;
    //                 var name = resDict.ContainsKey("name") ? resDict["name"]?.ToString() : null;
    //                 var learned = resDict.ContainsKey("learned") && Convert.ToBoolean(resDict["learned"]);

    //                 // Only return valid resources
    //                 if (link == null && name == null)
    //                 {
    //                     return null; // Skip resource if both link and name are null
    //                 }

    //                 return new ResourceDTO
    //                 {
    //                     Link = link,
    //                     Name = name,
    //                     Learned = learned
    //                 };
    //             }).Where(r => r != null).ToList() ?? new List<ResourceDTO>(); // Return an empty list if no valid resources

    //             var finishedResourcesCount = resources?.Count(r => r.Learned) ?? 0;
    //             var totalResources = resources?.Count ?? 0;
    //             var progressPercentage = totalResources > 0 ? (finishedResourcesCount / (float)totalResources) * 100 : 0;

    //             return new Lesson
    //             {
    //                 Id = lessonNode.Properties.ContainsKey("id") ? lessonNode.Properties["id"]?.As<string>() : "No ID available",
    //                 Name = lessonNode.Properties.ContainsKey("name") ? lessonNode.Properties["name"]?.As<string>() : "Unnamed Lesson",
    //                 Description = lessonNode.Properties.ContainsKey("description") ? lessonNode.Properties["description"]?.As<string>() : "No description available",
    //                 Resources = resources, // Return resources or an empty list
    //                 FinishedResourcesCount = finishedResourcesCount,
    //                 ProgressPercentage = progressPercentage
    //             };
    //         })
    //         .Where(lesson => lesson != null) // Filter out any null lessons
    //         .ToList(); // Convert to List<Lesson>
    // }

    public async Task<StudyPlanDTO?> GetStudyPlanByIdAsync(string studyPlanId, string currentUserId)
    {
        using var session = _neo4jDriver.AsyncSession();
        try
        {
            // Step 1. 从SQL拉取StudyPlan
            var studyPlan = await _sqlRepository.GetStudyPlanByIdAsync(studyPlanId);
            if (studyPlan == null) return null;

            var cypherQuery = @"
            MATCH (sp:StudyPlan {id: $studyPlanId})
            OPTIONAL MATCH (sp)-[hs:HAS_STEP]->(l:Lesson)
            OPTIONAL MATCH (l)-[:ASSOCIATED_WITH]->(lk:KnowledgeNode)
            OPTIONAL MATCH (sp)-[:ASSOCIATED_WITH]->(sk:KnowledgeNode)
            RETURN sp.id AS studyPlanId,
                   l.id AS lessonId,
                   hs.type AS stepType,
                   hs.order AS stepOrder,
                   collect(DISTINCT lk) AS lessonKnowledgeNodes,
                   collect(DISTINCT sk) AS studyPlanKnowledgeNodes
            ORDER BY hs.order";

            var result = await session.RunAsync(cypherQuery, new { studyPlanId });

            var recordList = await result.ToListAsync();
            if (recordList == null || recordList.Count == 0) return null;

            // 从主查询结果中提取 lessonIds，避免额外的 Neo4j 调用
            var lessonIds = recordList
                .Select(r => r["lessonId"]?.As<string>())
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();
            var lessons = await GetLessonsByIdsAsync(lessonIds);

            var studyPlanDetail = new StudyPlanDetail
            {
                Id = studyPlan.Id.ToString(),
                Title = studyPlan.Title,
                Introduction = new Introduction
                {
                    Description = studyPlan.Description,
                    AssociatedKnowledgeNodes = recordList.FirstOrDefault()?["studyPlanKnowledgeNodes"]
                        ?.As<List<INode>>()?.Select(MapNode).ToList() ?? new List<Node>()
                },
                Prerequisite = new List<Lesson>(),
                MainCurriculum = new List<Lesson>(),
                AdvancedTopics = new List<Lesson>()
            };

            var lessonMap = new Dictionary<string, Lesson>();
            foreach (var record in recordList)
            {
                var lessonId = record["lessonId"]?.As<string>();
                if (string.IsNullOrEmpty(lessonId)) continue;

                if (!lessons.TryGetValue(lessonId, out var sqlLesson) || sqlLesson == null) continue;

                var lesson = new Lesson
                {
                    Id = lessonId,
                    Name = sqlLesson.Title,
                    Description = sqlLesson.Description,
                    // 懒加载：Resources 不在此接口返回
                    Resources = null,
                    AssociatedKnowledgeNodes = record["lessonKnowledgeNodes"]
                        ?.As<List<INode>>()?.Select(MapNode).ToList() ?? new List<Node>()
                };
                lessonMap[lessonId] = lesson;

                var stepType = record["stepType"]?.As<string>();
                if (stepType == "PREREQUISITE")
                    studyPlanDetail.Prerequisite.Add(lesson);
                else if (stepType == "MAIN_CURRICULUM")
                    studyPlanDetail.MainCurriculum.Add(lesson);
                else if (stepType == "ADVANCED_TOPIC")
                    studyPlanDetail.AdvancedTopics.Add(lesson);
            }

            // 懒加载：不在此处计算整体进度（使用 Progress APIs 获取）

            return new StudyPlanDTO { StudyPlan = studyPlanDetail };
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    public class LessonDetailDto
    {
        public string? LessonId { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public List<ResourceDTO> Resources { get; set; } = new();
        public List<Node>? AssociatedKnowledgeNodes { get; set; }
    }

    public async Task<LessonDetailDto?> GetLessonDetailAsync(Guid planId, Guid lessonId, string userId)
    {
        using var session = _neo4jDriver.AsyncSession();
        // Basic lesson info from SQL
        var lessons = await GetLessonsByIdsAsync(new List<string> { lessonId.ToString() });
        if (!lessons.TryGetValue(lessonId.ToString(), out var sqlLesson) || sqlLesson == null)
            return null;

        // Fetch resources linked to the lesson and learned status for user
        var cypher = @"
MATCH (sp:StudyPlan {id:$planId})-[:HAS_STEP]->(l:Lesson {id:$lessonId})
OPTIONAL MATCH (l)-[:HAS_RESOURCE]->(r:Resource)
OPTIONAL MATCH (:User {id:$userId})-[cr:COMPLETED]->(r)
OPTIONAL MATCH (l)-[:ASSOCIATED_WITH]->(lk:KnowledgeNode)
RETURN l.id AS lessonId, collect(DISTINCT r.id) AS resourceIds, collect(DISTINCT lk) AS lessonKnowledgeNodes,
       collect({rid:r.id, learned:(cr IS NOT NULL)}) AS flags";
        var cursor = await session.RunAsync(cypher, new { planId = planId.ToString(), lessonId = lessonId.ToString(), userId });
        var rec = await cursor.SingleAsync();
        var ridObjs = rec["resourceIds"].As<List<object>>();
        var resourceIds = ridObjs.Where(x => x != null).Select(x => x.ToString()!).ToList();

        var resources = await GetResourcesByIdsAsync(resourceIds);

        // Build learned map
        var flags = rec["flags"].As<List<object>>();
        var learnedMap = new Dictionary<string, bool>();
        foreach (var f in flags)
        {
            if (f is IDictionary<string, object> d)
            {
                var rid = d.ContainsKey("rid") ? d["rid"]?.ToString() : null;
                var learned = false;
                try { learned = (d["learned"] as bool?) ?? false; } catch {}
                if (!string.IsNullOrEmpty(rid)) learnedMap[rid!] = learned;
            }
        }

        var dto = new LessonDetailDto
        {
            LessonId = sqlLesson.Id.ToString(),
            Title = sqlLesson.Title,
            Description = sqlLesson.Description,
            AssociatedKnowledgeNodes = rec["lessonKnowledgeNodes"]?.As<List<INode>>()?.Select(MapNode).ToList() ?? new List<Node>()
        };
        foreach (var rid in resourceIds)
        {
            if (resources.TryGetValue(rid, out var res) && res != null)
            {
                dto.Resources.Add(new ResourceDTO
                {
                    Id = res.Id.ToString(),
                    Name = res.Name,
                    Link = res.Link,
                    Learned = learnedMap.TryGetValue(rid, out var v) && v
                });
            }
        }

        return dto;
    }

    public async Task<bool> DeleteStudyPlanAsync(string studyPlanId, string currentUserId)
    {
        using var session = _neo4jDriver.AsyncSession();
        try
        {
            // Step 1. 从Neo4j查一下当前StudyPlan是否存在，并且是这个用户创建的
            var exists = await session.ExecuteReadAsync(async tx =>
            {
                var result = await tx.RunAsync(@"
                MATCH (u:User {id: $currentUserId})-[:CREATED]->(p:StudyPlan {id: $studyPlanId})
                RETURN p.id AS id
            ", new { currentUserId, studyPlanId });

                var records = await result.ToListAsync();
                var record = records.SingleOrDefault();
                return record != null;
            });

            if (!exists)
            {
                _logger.LogWarning("StudyPlan {studyPlanId} not found or not created by user {currentUserId}.", studyPlanId, currentUserId);
                return false;
            }

            // Step 2. 删除Neo4j中StudyPlan的关系
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                MATCH (p:StudyPlan {id: $studyPlanId})
                OPTIONAL MATCH (p)-[r]-()
                DELETE r
            ", new { studyPlanId });
            });

            // Step 3. 删除SQL中的StudyPlan
            await _sqlRepository.DeleteStudyPlanByIdAsync(studyPlanId);

            // Step 4. 删除Neo4j中StudyPlan节点本身
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"
                MATCH (p:StudyPlan {id: $studyPlanId})
                DELETE p
            ", new { studyPlanId });
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete StudyPlan {studyPlanId}", studyPlanId);
            return false;
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
