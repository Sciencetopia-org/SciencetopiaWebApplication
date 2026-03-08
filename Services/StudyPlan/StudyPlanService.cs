using System;
using Neo4j.Driver;
using System.Linq;
using Sciencetopia.Constants;
using Sciencetopia.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

public class StudyPlanService
{
    private readonly IDriver _neo4jDriver;
    private readonly ILogger<StudyPlanService> _logger;
    private readonly IStudyPlanRepository _sqlRepository;
    private readonly ITagRepository? _tagRepo; // optional via DI in controller methods
    private readonly ITagResolutionService? _tagResolution;

    public StudyPlanService(IDriver neo4jDriver, ILogger<StudyPlanService> logger, IStudyPlanRepository sqlRepository)
    {
        _neo4jDriver = neo4jDriver;
        _logger = logger;
        _sqlRepository = sqlRepository;
    }

    private async Task TryAutoTagAsync(string planStableId, StudyPlanDetail planDto, string userId)
    {
        if (_tagRepo == null || _tagResolution == null)
        {
            _logger.LogDebug("Skipping auto-tag for plan {PlanId} due to missing tag services.", planStableId);
            return;
        }

        if (!Guid.TryParse(planStableId, out var stableGuid))
        {
            return;
        }

        var suggestions = await SuggestTagsFromDetailAsync(planDto);

        var planTagNames = suggestions.PlanTags
            .Select(t => t.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        if (planTagNames.Count > 0)
        {
            var planTagStableIds = await ResolveTagNamesToStableIdsAsync(planTagNames, userId);
            var versionNumber = planDto.VersionNumber > 0 ? planDto.VersionNumber : 1;
            await _sqlRepository.ReplacePlanTagAssignmentsAsync(stableGuid, versionNumber, planTagStableIds);
        }

        foreach (var lessonSuggestion in suggestions.Lessons)
        {
            var names = lessonSuggestion.Tags
                .Select(t => t.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();

            if (names.Count == 0)
            {
                continue;
            }

            if (!Guid.TryParse(lessonSuggestion.LessonId, out var lessonVersionId))
            {
                continue;
            }

            var lessonEntity = await _sqlRepository.GetLessonByIdAsync(lessonVersionId);
            if (lessonEntity == null)
            {
                continue;
            }

            var tagIds = await ResolveTagNamesToStableIdsAsync(names, userId);
            await ReplaceLessonTagsInGraphAsync(lessonEntity.Id, tagIds);
        }
    }

    // Secondary constructor for scenarios where tag services are injected explicitly
    public StudyPlanService(
        IDriver neo4jDriver,
        ILogger<StudyPlanService> logger,
        IStudyPlanRepository sqlRepository,
        ITagRepository tagRepository,
        ITagResolutionService tagResolution)
        : this(neo4jDriver, logger, sqlRepository)
    {
        _tagRepo = tagRepository;
        _tagResolution = tagResolution;
    }

    public async Task<string?> SaveStudyPlanAsync(StudyPlanDTO studyPlanDTO, string userId, bool autoTag = false)
    {
        if (studyPlanDTO?.StudyPlan == null)
        {
            return null;
        }

        var planDto = studyPlanDTO.StudyPlan;
        var planStableId = Guid.NewGuid();
        var planVersionId = Guid.NewGuid();
        var utcNow = DateTime.UtcNow;
        var utcOffsetNow = DateTimeOffset.UtcNow;

        var planEntity = new StudyPlanEntity
        {
            Id = planVersionId,
            StableId = planStableId,
            VersionNumber = 1,
            Status = "Active",
            IsCurrent = true,
            PublishedAt = utcOffsetNow,
            CreatedAt = utcOffsetNow,
            CreatedBy = userId,
            ApprovedAt = utcOffsetNow,
            ApprovedBy = userId,
            Title = planDto.Title,
            Description = planDto.Introduction?.Description,
            MetadataJson = BuildPlanMetadataJson(planDto),
            CreatorId = Guid.Parse(userId),
            CreatedDate = utcNow,
            UpdatedDate = utcNow
        };

        await _sqlRepository.InsertStudyPlanAsync(planEntity);

        var snapshots = await UpdateLessonsAsync(planStableId, planVersionId, planEntity.VersionNumber, planDto, createNewVersion: false, userId, utcOffsetNow);
        await _sqlRepository.ReplacePlanLessonSnapshotsAsync(planStableId, planEntity.VersionNumber, snapshots);

        // Build and persist lockfile for published v1
        var lockLessons = snapshots
            .OrderBy(s => s.StepOrder)
            .Select((s, i) => new Sciencetopia.DTOs.StudyPlanLockfileItem
            {
                LessonStableId = s.LessonStableId,
                LessonVersionNumber = s.LessonVersionNumber,
                StepType = s.StepType,
                Index = i + 1
            })
            .ToList();
        var lockfile = new Sciencetopia.DTOs.StudyPlanLockfile { Lessons = lockLessons };
        planEntity.LockfileJson = JsonSerializer.Serialize(lockfile);
        await _sqlRepository.UpdateStudyPlanAsync(planEntity);

        await UpsertStudyPlanGraphAsync(planStableId.ToString(), planDto, planEntity.VersionNumber);

        if (autoTag)
        {
            await TryAutoTagAsync(planStableId.ToString(), planDto, userId);
        }

        planDto.Id = planVersionId.ToString();
        planDto.StableId = planStableId.ToString();
        planDto.VersionNumber = planEntity.VersionNumber;
        planDto.IsCurrent = true;
        planDto.Status = planEntity.Status;
        planDto.PublishedAt = planEntity.PublishedAt;

        return planVersionId.ToString();
    }

    private IEnumerable<(Lesson lesson, string type)> GetLessonsWithType(StudyPlanDetail studyPlan)
    {
        foreach (var lesson in studyPlan.Prerequisite)
            yield return (lesson, StudyPlanStepTypes.Prerequisite);
        foreach (var lesson in studyPlan.MainCurriculum)
            yield return (lesson, StudyPlanStepTypes.MainCurriculum);
        foreach (var lesson in studyPlan.AdvancedTopics)
            yield return (lesson, StudyPlanStepTypes.AdvancedTopic);
    }

    private string BuildPlanMetadataJson(StudyPlanDetail? studyPlan)
    {
        var counts = new
        {
            prerequisites = studyPlan?.Prerequisite?.Count ?? 0,
            mainCurriculum = studyPlan?.MainCurriculum?.Count ?? 0,
            advancedTopics = studyPlan?.AdvancedTopics?.Count ?? 0
        };
        return JsonSerializer.Serialize(counts);
    }

    private async Task<List<StudyPlanLessonSnapshot>> UpdateLessonsAsync(Guid planStableId, Guid planVersionId, int planVersionNumber, StudyPlanDetail? studyPlan, bool createNewVersion, string userId, DateTimeOffset timestamp)
    {
        var snapshots = new List<StudyPlanLessonSnapshot>();
        if (studyPlan == null)
        {
            return snapshots;
        }

        var orderedLessons = GetLessonsWithType(studyPlan).ToList();
        var order = 0;

        foreach (var (lesson, stepType) in orderedLessons)
        {
            order++;
            var lessonKind = stepType;
            var lessonMetadataJson = JsonSerializer.Serialize(new { stepType });

            Guid lessonStableId;
            int targetVersionNumber;
            Guid targetVersionId;

            LessonEntity? currentLessonEntity = null;
            if (!string.IsNullOrWhiteSpace(lesson.StableId) && Guid.TryParse(lesson.StableId, out var parsedStableId))
            {
                lessonStableId = parsedStableId;
                currentLessonEntity = await _sqlRepository.GetLessonByStableIdAsync(lessonStableId);
            }
            else if (!string.IsNullOrWhiteSpace(lesson.Id) && Guid.TryParse(lesson.Id, out var existingVersionId))
            {
                currentLessonEntity = await _sqlRepository.GetLessonByIdAsync(existingVersionId);
                lessonStableId = currentLessonEntity?.StableId ?? Guid.NewGuid();
            }
            else
            {
                lessonStableId = Guid.NewGuid();
            }

            if (createNewVersion)
            {
                if (currentLessonEntity != null)
                {
                    currentLessonEntity.IsCurrent = false;
                    currentLessonEntity.Status = "Archived";
                    currentLessonEntity.RetiredAt = timestamp;
                    currentLessonEntity.UpdatedDate = DateTime.UtcNow;
                    await _sqlRepository.UpdateLessonAsync(currentLessonEntity);
                }

                targetVersionNumber = await _sqlRepository.GetNextLessonVersionNumberAsync(lessonStableId);
                targetVersionId = Guid.NewGuid();

                await _sqlRepository.InsertLessonAsync(new LessonEntity
                {
                    Id = targetVersionId,
                    StableId = lessonStableId,
                    VersionNumber = targetVersionNumber,
                    Status = "Active",
                    IsCurrent = true,
                    PublishedAt = timestamp,
                    ApprovedAt = timestamp,
                    ApprovedBy = userId,
                    CreatedAt = timestamp,
                    CreatedBy = userId,
                    Title = lesson.Name,
                    Description = lesson.Description,
                    StudyPlanId = planVersionId,
                    OrderIndex = order,
                    Kind = lessonKind,
                    MetadataJson = lessonMetadataJson,
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow
                });
            }
            else
            {
                if (currentLessonEntity == null)
                {
                    targetVersionNumber = 1;
                    targetVersionId = Guid.NewGuid();

                    await _sqlRepository.InsertLessonAsync(new LessonEntity
                    {
                        Id = targetVersionId,
                        StableId = lessonStableId,
                        VersionNumber = targetVersionNumber,
                        Status = "Active",
                        IsCurrent = true,
                        PublishedAt = timestamp,
                        ApprovedAt = timestamp,
                        ApprovedBy = userId,
                        CreatedAt = timestamp,
                        CreatedBy = userId,
                        Title = lesson.Name,
                        Description = lesson.Description,
                        StudyPlanId = planVersionId,
                        OrderIndex = order,
                        Kind = lessonKind,
                        MetadataJson = lessonMetadataJson,
                        CreatedDate = DateTime.UtcNow,
                        UpdatedDate = DateTime.UtcNow
                    });
                }
                else
                {
                    targetVersionNumber = currentLessonEntity.VersionNumber;
                    targetVersionId = currentLessonEntity.Id;

                    currentLessonEntity.Title = lesson.Name;
                    currentLessonEntity.Description = lesson.Description;
                    currentLessonEntity.StudyPlanId ??= planVersionId;
                    currentLessonEntity.OrderIndex = order;
                    currentLessonEntity.Kind = lessonKind;
                    currentLessonEntity.MetadataJson = lessonMetadataJson;
                    currentLessonEntity.UpdatedDate = DateTime.UtcNow;
                    currentLessonEntity.CreatedAt ??= timestamp;
                    currentLessonEntity.CreatedBy ??= userId;
                    await _sqlRepository.UpdateLessonAsync(currentLessonEntity);
                }
            }

            lesson.StableId = lessonStableId.ToString();
            lesson.VersionNumber = targetVersionNumber;
            lesson.Id = targetVersionId.ToString();

            if (lesson.Resources != null)
            {
                foreach (var resource in lesson.Resources)
                {
                    if (!string.IsNullOrWhiteSpace(resource.Id) && Guid.TryParse(resource.Id, out var resourceGuid))
                    {
                        await _sqlRepository.UpdateResourceAsync(new Resource
                        {
                            Id = resourceGuid,
                            Name = resource.Name,
                            Link = resource.Link
                        });
                    }
                    else
                    {
                        var newResourceId = Guid.NewGuid();
                        resource.Id = newResourceId.ToString();
                        await _sqlRepository.InsertResourceAsync(new Resource
                        {
                            Id = newResourceId,
                            Name = resource.Name,
                            Link = resource.Link
                        });
                    }
                }
            }

            snapshots.Add(new StudyPlanLessonSnapshot
            {
                StudyPlanStableId = planStableId,
                StudyPlanVersionNumber = planVersionNumber,
                LessonStableId = lessonStableId,
                LessonVersionNumber = lesson.VersionNumber,
                StepType = stepType,
                StepOrder = order
            });
        }

        return snapshots;
    }

    private async Task UpsertStudyPlanGraphAsync(string planStableId, StudyPlanDetail studyPlan, int versionNumber)
    {
        using var session = _neo4jDriver.AsyncSession();

        await session.ExecuteWriteAsync(async transaction =>
        {
            await transaction.RunAsync(@"
                MERGE (p:StudyPlan {id: $id})
                SET p.title = $title,
                    p.description = $description,
                    p.versionNumber = $versionNumber,
                    p.updatedAt = timestamp()
            ", new
            {
                id = planStableId,
                title = studyPlan.Title,
                description = studyPlan.Introduction?.Description,
                versionNumber
            });

            await transaction.RunAsync(@"MATCH (p:StudyPlan {id: $id})-[r:HAS_STEP]->(:Lesson) DELETE r", new { id = planStableId });

            int orderCounter = 0;
            foreach (var (lesson, type) in GetLessonsWithType(studyPlan))
            {
                if (string.IsNullOrWhiteSpace(lesson.Id))
                {
                    continue;
                }

                orderCounter++;

                await transaction.RunAsync(@"
                    MERGE (l:Lesson {id: $lessonId})
                    SET l.name = $name,
                        l.description = $description
                ", new
                {
                    lessonId = lesson.Id,
                    name = lesson.Name,
                    description = lesson.Description
                });

                await transaction.RunAsync(@"
                    MATCH (p:StudyPlan {id: $planId}), (l:Lesson {id: $lessonId})
                    MERGE (p)-[:HAS_STEP {type: $type, order: $order}]->(l)
                ", new
                {
                    planId = planStableId,
                    lessonId = lesson.Id,
                    type,
                    order = orderCounter
                });

                await transaction.RunAsync(@"MATCH (l:Lesson {id: $lessonId})-[r:HAS_RESOURCE]->(:Resource) DELETE r", new { lessonId = lesson.Id });

                if (lesson.Resources != null)
                {
                    foreach (var resource in lesson.Resources)
                    {
                        if (string.IsNullOrWhiteSpace(resource.Id)) continue;
                        await transaction.RunAsync(@"
                            MERGE (r:Resource {id: $resourceId})
                            SET r.name = $resourceName,
                                r.link = $resourceLink
                        ", new
                        {
                            resourceId = resource.Id,
                            resourceName = resource.Name,
                            resourceLink = resource.Link
                        });

                        await transaction.RunAsync(@"
                            MATCH (l:Lesson {id: $lessonId}), (r:Resource {id: $resourceId})
                            MERGE (l)-[:HAS_RESOURCE]->(r)
                        ", new
                        {
                            lessonId = lesson.Id,
                            resourceId = resource.Id
                        });
                    }
                }

                await transaction.RunAsync(@"MATCH (l:Lesson {id:$lessonId})-[r:ASSOCIATED_WITH]->(:KnowledgeNode) DELETE r", new { lessonId = lesson.Id });
                await transaction.RunAsync(@"
MATCH (l:Lesson {id:$lessonId})-[:HAS_RESOURCE]->(r:Resource)<-[:HAS_RESOURCE]-(k:KnowledgeNode)
MERGE (l)-[:ASSOCIATED_WITH]->(k)", new { lessonId = lesson.Id });
            }

            await transaction.RunAsync(@"MATCH (:StudyPlan {id: $planId})-[r:ASSOCIATED_WITH]->(:KnowledgeNode) DELETE r", new { planId = planStableId });
            if (studyPlan.Introduction?.AssociatedKnowledgeNodes != null)
            {
                foreach (var knowledgeNode in studyPlan.Introduction.AssociatedKnowledgeNodes)
                {
                    var knId = knowledgeNode?.Properties?.Id ?? knowledgeNode?.Properties?.Link;
                    if (string.IsNullOrWhiteSpace(knId)) continue;
                    await transaction.RunAsync(@"
                        MATCH (p:StudyPlan {id: $planId}), (k:KnowledgeNode {id: $knId})
                        MERGE (p)-[:ASSOCIATED_WITH]->(k)
                    ", new { planId = planStableId, knId });
                }
            }
        });
    }

    public async Task<(bool ok, string? studyPlanId)> UpdateStudyPlanAsync(StudyPlanDTO updatedStudyPlan, string userId, bool createNewVersion)
    {
        if (updatedStudyPlan?.StudyPlan?.Id == null)
        {
            return (false, null);
        }

        if (!Guid.TryParse(updatedStudyPlan.StudyPlan.Id, out var planId))
        {
            return (false, null);
        }

        var existingPlan = await _sqlRepository.GetStudyPlanByIdAsync(updatedStudyPlan.StudyPlan.Id);
        if (existingPlan == null)
        {
            return (false, null);
        }

        var stableId = existingPlan.StableId == Guid.Empty ? planId : existingPlan.StableId;
        var utcNow = DateTime.UtcNow;
        var offsetNow = DateTimeOffset.UtcNow;

        StudyPlanEntity targetVersion;

        if (createNewVersion)
        {
            existingPlan.Status = "Archived";
            existingPlan.IsCurrent = false;
            existingPlan.RetiredAt = offsetNow;
            existingPlan.UpdatedDate = utcNow;
            await _sqlRepository.UpdateStudyPlanAsync(existingPlan);

            var nextVersion = await _sqlRepository.GetNextStudyPlanVersionNumberAsync(stableId);
            targetVersion = new StudyPlanEntity
            {
                Id = Guid.NewGuid(),
                StableId = stableId,
                VersionNumber = nextVersion,
                Status = "Active",
                IsCurrent = true,
                PublishedAt = offsetNow,
                ApprovedAt = offsetNow,
                ApprovedBy = userId,
                CreatedAt = offsetNow,
                CreatedBy = existingPlan.CreatedBy ?? userId,
                Title = updatedStudyPlan.StudyPlan.Title ?? existingPlan.Title,
                Description = updatedStudyPlan.StudyPlan.Introduction?.Description ?? existingPlan.Description,
                MetadataJson = BuildPlanMetadataJson(updatedStudyPlan.StudyPlan),
                CreatorId = existingPlan.CreatorId,
                CreatedDate = existingPlan.CreatedDate,
                UpdatedDate = utcNow
            };

            await _sqlRepository.InsertStudyPlanAsync(targetVersion);
        }
        else
        {
            existingPlan.StableId = stableId;
            existingPlan.Title = updatedStudyPlan.StudyPlan.Title ?? existingPlan.Title;
            existingPlan.Description = updatedStudyPlan.StudyPlan.Introduction?.Description ?? existingPlan.Description;
            existingPlan.MetadataJson = BuildPlanMetadataJson(updatedStudyPlan.StudyPlan);
            existingPlan.UpdatedDate = utcNow;
            existingPlan.CreatedAt ??= offsetNow;
            existingPlan.CreatedBy ??= userId;
            targetVersion = existingPlan;
            await _sqlRepository.UpdateStudyPlanAsync(targetVersion);
        }

        var lessonSnapshots = await UpdateLessonsAsync(stableId, targetVersion.Id, targetVersion.VersionNumber, updatedStudyPlan.StudyPlan, createNewVersion, userId, offsetNow);
        await _sqlRepository.ReplacePlanLessonSnapshotsAsync(stableId, targetVersion.VersionNumber, lessonSnapshots);
        // If version is published/current, rebuild lockfile
        if (targetVersion.IsCurrent || string.Equals(targetVersion.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            var lockLessons = lessonSnapshots
                .OrderBy(s => s.StepOrder)
                .Select((s, i) => new Sciencetopia.DTOs.StudyPlanLockfileItem
                {
                    LessonStableId = s.LessonStableId,
                    LessonVersionNumber = s.LessonVersionNumber,
                    StepType = s.StepType,
                    Index = i + 1
                })
                .ToList();
            var lockfile = new Sciencetopia.DTOs.StudyPlanLockfile { Lessons = lockLessons };
            targetVersion.LockfileJson = JsonSerializer.Serialize(lockfile);
            await _sqlRepository.UpdateStudyPlanAsync(targetVersion);
        }
        await UpsertStudyPlanGraphAsync(stableId.ToString(), updatedStudyPlan.StudyPlan, targetVersion.VersionNumber);

        updatedStudyPlan.StudyPlan.Id = targetVersion.Id.ToString();
        updatedStudyPlan.StudyPlan.StableId = stableId.ToString();
        updatedStudyPlan.StudyPlan.VersionNumber = targetVersion.VersionNumber;
        updatedStudyPlan.StudyPlan.IsCurrent = targetVersion.IsCurrent;
        updatedStudyPlan.StudyPlan.Status = targetVersion.Status;
        updatedStudyPlan.StudyPlan.PublishedAt = targetVersion.PublishedAt;

        return (true, targetVersion.Id.ToString());
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

    public async Task<bool> SetStudyPlanPrivacyAsync(string userId, string planStableId, string privacy)
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
                    studyPlanId = planStableId,
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

    // ---------------------------------------------------------------------
    // Query helpers and controller-facing operations

    private static StudyPlanDTO MapPlanEntityToDto(StudyPlanEntity entity)
    {
        var stableId = entity.StableId == Guid.Empty ? entity.Id : entity.StableId;
        return new StudyPlanDTO
        {
            StudyPlan = new StudyPlanDetail
            {
                Id = entity.Id.ToString(),
                StableId = stableId.ToString(),
                VersionNumber = entity.VersionNumber,
                Title = entity.Title,
                Status = entity.Status,
                IsCurrent = entity.IsCurrent,
                PublishedAt = entity.PublishedAt,
                Introduction = new Introduction { Description = entity.Description },
                Prerequisite = new List<Lesson>(),
                MainCurriculum = new List<Lesson>(),
                AdvancedTopics = new List<Lesson>()
            }
        };
    }

    private static Lesson MapLessonEntityToDto(LessonEntity entity)
    {
        return new Lesson
        {
            Id = entity.Id.ToString(),
            StableId = (entity.StableId == Guid.Empty ? entity.Id : entity.StableId).ToString(),
            VersionNumber = entity.VersionNumber,
            Name = entity.Title,
            Description = entity.Description,
            Resources = new List<ResourceDTO>(),
            Tags = new List<TagDTO>()
        };
    }

    public async Task<IEnumerable<StudyPlanDTO>> GetStudyPlansByUserIdAsync(string requesterId, string targetUserId)
    {
        var plans = await _sqlRepository.GetStudyPlansByUserIdAsync(targetUserId);
        return plans.Select(MapPlanEntityToDto);
    }

    public async Task<StudyPlanDTO?> GetStudyPlanByIdAsync(string studyPlanId, string requesterId)
    {
        if (!Guid.TryParse(studyPlanId, out var planGuid))
        {
            return null;
        }

        var entity = await _sqlRepository.GetStudyPlanByIdAsync(studyPlanId);
        if (entity == null)
        {
            return null;
        }

        var detail = await BuildPlanDetailAsync(entity, requesterId);
        return new StudyPlanDTO { StudyPlan = detail };
    }

    public async Task<bool> DeleteStudyPlanAsync(string studyPlanTitle, string userId)
    {
        if (string.IsNullOrWhiteSpace(studyPlanTitle))
        {
            return false;
        }

        var plans = await _sqlRepository.GetStudyPlansByUserIdAsync(userId);
        var target = plans.FirstOrDefault(p => string.Equals(p.Title, studyPlanTitle, StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            return false;
        }

        await _sqlRepository.DeleteStudyPlanByIdAsync(target.Id.ToString());

        // Best-effort Neo4j cleanup
        try
        {
            var stableId = target.StableId == Guid.Empty ? target.Id : target.StableId;
            await using var session = _neo4jDriver.AsyncSession();
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"MATCH (p:StudyPlan {id:$id}) DETACH DELETE p", new { id = stableId.ToString() });
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove study plan {PlanId} from Neo4j", target.Id);
        }

        return true;
    }

    public async Task<List<TagDTO>> GetPlanTagsAsync(string planId)
    {
        var context = await ResolvePlanContextAsync(planId);
        if (context == null || _tagRepo == null)
        {
            return new List<TagDTO>();
        }

        var tagStableIds = await _sqlRepository.GetPlanTagStableIdsAsync(context.Value.StableId, context.Value.VersionNumber);
        return await BuildTagDtosAsync(tagStableIds);
    }

    public async Task<bool> UpdatePlanTagsAsync(string planId, IEnumerable<Guid>? tagIds = null, IEnumerable<string>? newTagNames = null, string? userId = null)
    {
        var context = await ResolvePlanContextAsync(planId);
        if (context == null || _tagRepo == null)
        {
            return false;
        }

        var finalStableIds = new HashSet<Guid>();

        if (tagIds != null)
        {
            var resolved = await ResolveTagIdsAsync(tagIds);
            foreach (var id in resolved) finalStableIds.Add(id);
        }

        if (newTagNames != null && newTagNames.Any())
        {
            var created = await ResolveTagNamesToStableIdsAsync(newTagNames, userId ?? "system");
            foreach (var id in created) finalStableIds.Add(id);
        }

        await _sqlRepository.ReplacePlanTagAssignmentsAsync(context.Value.StableId, context.Value.VersionNumber, finalStableIds);
        return true;
    }

    public async Task<List<TagDTO>> GetLessonTagsAsync(string planId, string lessonId)
    {
        if (!Guid.TryParse(lessonId, out var lessonGuid) || _tagRepo == null)
        {
            return new List<TagDTO>();
        }

        var lesson = await _sqlRepository.GetLessonByIdAsync(lessonGuid);
        if (lesson == null)
        {
            return new List<TagDTO>();
        }

        var tagStableIds = await GetLessonTagStableIdsFromGraphAsync(lesson.Id);
        return await BuildTagDtosAsync(tagStableIds);
    }

    public async Task<bool> UpdateLessonTagsAsync(string planId, string lessonId, IEnumerable<Guid>? tagIds = null, IEnumerable<string>? newTagNames = null, string? userId = null)
    {
        if (!Guid.TryParse(lessonId, out var lessonGuid) || _tagRepo == null)
        {
            return false;
        }

        var lesson = await _sqlRepository.GetLessonByIdAsync(lessonGuid);
        if (lesson == null)
        {
            return false;
        }

        var finalStableIds = new HashSet<Guid>();

        if (tagIds != null)
        {
            var resolved = await ResolveTagIdsAsync(tagIds);
            foreach (var id in resolved) finalStableIds.Add(id);
        }

        if (newTagNames != null && newTagNames.Any())
        {
            var created = await ResolveTagNamesToStableIdsAsync(newTagNames, userId ?? "system");
            foreach (var id in created) finalStableIds.Add(id);
        }

        await ReplaceLessonTagsInGraphAsync(lesson.Id, finalStableIds);
        return true;
    }

    public async Task<List<TagDTO>> SuggestLessonTagsAsync(string planId, string lessonId)
    {
        if (!Guid.TryParse(lessonId, out var lessonGuid) || _tagRepo == null)
        {
            return new List<TagDTO>();
        }

        var lesson = await _sqlRepository.GetLessonByIdAsync(lessonGuid);
        if (lesson == null)
        {
            return new List<TagDTO>();
        }

        var keywords = ExtractKeywords(lesson.Title, lesson.Description);
        return await SuggestTagsFromKeywordsAsync(keywords, 10);
    }

    public async Task<List<TagDTO>> SuggestPlanTagsAsync(string planId)
    {
        var context = await ResolvePlanContextAsync(planId);
        if (context == null || _tagRepo == null)
        {
            return new List<TagDTO>();
        }

        var plan = context.Value.Entity;
        var keywords = ExtractKeywords(plan.Title, plan.Description);
        return await SuggestTagsFromKeywordsAsync(keywords, 10);
    }

    public async Task<object> SuggestFromPayloadAsync(StudyPlanDTO payload)
    {
        if (_tagRepo == null || payload?.StudyPlan == null)
        {
            return new { planTags = Array.Empty<TagDTO>(), lessons = Array.Empty<object>() };
        }

        var bundle = await SuggestTagsFromDetailAsync(payload.StudyPlan);
        var lessons = bundle.Lessons.Select(l => new
        {
            lessonId = l.LessonId,
            key = l.Key,
            tags = l.Tags
        }).ToList();

        return new { planTags = bundle.PlanTags, lessons };
    }

    public async Task<Lesson?> GetLessonDetailAsync(Guid planId, Guid lessonId, string userId)
    {
        var lessonEntity = await _sqlRepository.GetLessonByIdAsync(lessonId);
        if (lessonEntity == null)
        {
            return null;
        }

        var dto = MapLessonEntityToDto(lessonEntity);
        dto.Resources = await LoadLessonResourcesAsync(lessonEntity.Id);

        var lessonTags = await GetLessonTagStableIdsFromGraphAsync(lessonEntity.Id);
        dto.Tags = await BuildTagDtosAsync(lessonTags);

        return dto;
    }

    // ---------------------------------------------------------------------
    // Internal helpers

    private async Task<(Guid StableId, int VersionNumber, StudyPlanEntity Entity)?> ResolvePlanContextAsync(string planId)
    {
        if (!Guid.TryParse(planId, out var planGuid))
        {
            return null;
        }

        var entity = await _sqlRepository.GetStudyPlanByIdAsync(planId);
        if (entity == null)
        {
            return null;
        }

        var stableId = entity.StableId == Guid.Empty ? entity.Id : entity.StableId;
        return (stableId, entity.VersionNumber, entity);
    }

    private async Task<StudyPlanDetail> BuildPlanDetailAsync(StudyPlanEntity entity, string requesterId)
    {
        var stableId = entity.StableId == Guid.Empty ? entity.Id : entity.StableId;
        var detail = new StudyPlanDetail
        {
            Id = entity.Id.ToString(),
            StableId = stableId.ToString(),
            VersionNumber = entity.VersionNumber,
            Title = entity.Title,
            Status = entity.Status,
            IsCurrent = entity.IsCurrent,
            PublishedAt = entity.PublishedAt,
            Introduction = new Introduction { Description = entity.Description },
            Prerequisite = new List<Lesson>(),
            MainCurriculum = new List<Lesson>(),
            AdvancedTopics = new List<Lesson>(),
            Tags = new List<TagDTO>()
        };
        // Prepare lesson references (stableId + versionNo, and type) either from lockfile or snapshots
        var desired = new List<(Guid LessonStableId, int VersionNumber, string StepType, int StepOrder)>();
        var lockfile = TryParseLockfile(entity.LockfileJson);
        if (lockfile?.Lessons?.Count > 0)
        {
            foreach (var (it, idx) in lockfile.Lessons.Select((x, i) => (x, i + 1)))
            {
                desired.Add((it.LessonStableId, it.LessonVersionNumber, it.StepType ?? StudyPlanStepTypes.MainCurriculum, idx));
            }
        }
        else
        {
            var snapshots = await _sqlRepository.GetPlanLessonSnapshotsAsync(stableId, entity.VersionNumber);
            foreach (var s in snapshots.OrderBy(x => x.StepOrder))
            {
                desired.Add((s.LessonStableId, s.LessonVersionNumber, s.StepType, s.StepOrder));
            }
            if (desired.Count == 0)
            {
                // Fallback to graph ordering if no snapshots and no lockfile
                try
                {
                    using var session = _neo4jDriver.AsyncSession();
                    var records = await session.ExecuteReadAsync(async tx =>
                    {
                        var cypher = @"MATCH (p:StudyPlan {id:$pid})-[r:HAS_STEP]->(l:Lesson)
                                       RETURN l.id AS lessonId, r.type AS type, r.order AS ord
                                       ORDER BY ord ASC";
                        var cursor = await tx.RunAsync(cypher, new { pid = stableId.ToString() });
                        return await cursor.ToListAsync();
                    });
                    // When reading from graph we only have lesson version ids
                    var lessonIds = new List<Guid>();
                    var types = new List<string>();
                    foreach (var rec in records)
                    {
                        var lidStr = rec["lessonId"].As<string?>();
                        var type = rec["type"].As<string?>() ?? StudyPlanStepTypes.MainCurriculum;
                        if (Guid.TryParse(lidStr, out var lid)) { lessonIds.Add(lid); types.Add(type); }
                    }
                    // Load lessons by version ids in a single SQL call
                    var lessonEntities = await _sqlRepository.GetLessonsByIdsAsync(lessonIds.Select(x => x.ToString()).ToList());
                    var byId = lessonEntities.ToDictionary(x => x.Id, x => x);
                    for (int i = 0; i < lessonIds.Count; i++)
                    {
                        if (byId.TryGetValue(lessonIds[i], out var ent))
                        {
                            var st = ent.StableId == Guid.Empty ? ent.Id : ent.StableId;
                            desired.Add((st, ent.VersionNumber, types[i], i + 1));
                        }
                    }
                }
                catch { /* ignore */ }
            }
        }

        if (desired.Count > 0)
        {
            // Batch load all lessons for involved stableIds and select required versions
            var stableSet = desired.Select(d => d.LessonStableId).Distinct().ToList();
            var candidates = await _sqlRepository.GetLessonsByStableIdsAsync(stableSet);
            var picked = new List<(LessonEntity Entity, string StepType, int StepOrder)>();
            // Build lookup of (stableId, versionNo) -> lesson
            var byStable = candidates.GroupBy(l => l.StableId).ToDictionary(g => g.Key, g => g.ToDictionary(x => x.VersionNumber, x => x));
            foreach (var d in desired)
            {
                if (byStable.TryGetValue(d.LessonStableId, out var versions) && versions.TryGetValue(d.VersionNumber, out var ent))
                {
                    picked.Add((ent, d.StepType, d.StepOrder));
                }
            }

            // Batch load resources from graph for all picked lesson version ids
            var resourcesMap = await LoadLessonResourcesBatchAsync(picked.Select(p => p.Entity.Id));

            // Batch load tags from graph for all lesson version ids, then resolve names once
            var tagByLessonId = await GetLessonTagStableIdsByLessonIdsAsync(picked.Select(p => p.Entity.Id));
            var allTagIds = tagByLessonId.Values.SelectMany(x => x).Distinct().ToList();
            var tagNameMap = await BuildTagNameMapAsync(allTagIds);

            // Assemble DTOs in order
            foreach (var item in picked.OrderBy(x => x.StepOrder))
            {
                var dto = MapLessonEntityToDto(item.Entity);
                dto.Resources = resourcesMap.TryGetValue(item.Entity.Id, out var resList) ? resList : new List<ResourceDTO>();
                if (tagByLessonId.TryGetValue(item.Entity.Id, out var lessonTagIds) && lessonTagIds.Count > 0)
                {
                    dto.Tags = lessonTagIds.Select(id => new TagDTO { Id = id, Name = tagNameMap.TryGetValue(id, out var nm) ? nm : null }).ToList();
                }
                AddLessonByType(detail, item.StepType, dto);
            }
        }

        var planTags = await _sqlRepository.GetPlanTagStableIdsAsync(stableId, entity.VersionNumber);
        detail.Tags = await BuildTagDtosAsync(planTags);

        return detail;
    }

    private async Task<Dictionary<Guid, List<ResourceDTO>>> LoadLessonResourcesBatchAsync(IEnumerable<Guid> lessonVersionIds)
    {
        var ids = lessonVersionIds?.Distinct().ToList() ?? new List<Guid>();
        var result = new Dictionary<Guid, List<ResourceDTO>>();
        if (ids.Count == 0) return result;

        // 1) Read lesson->resource ids from graph in one pass
        var linkRows = new List<(Guid LessonId, string ResourceId, string? OrdKey)>();
        await using (var session = _neo4jDriver.AsyncSession())
        {
            var records = await session.ExecuteReadAsync(async tx =>
            {
                var cypher = @"MATCH (l:Lesson)-[:HAS_RESOURCE]->(r:Resource)
                               WHERE l.id IN $lessonIds
                               RETURN l.id AS lid, r.id AS rid, coalesce(r.order, r.name, r.title) AS ord";
                var cursor = await tx.RunAsync(cypher, new { lessonIds = ids.Select(x => x.ToString()).ToList() });
                return await cursor.ToListAsync();
            });
            foreach (var rec in records)
            {
                var lid = rec["lid"].As<string?>();
                var rid = rec["rid"].As<string?>();
                var ord = rec["ord"].As<string?>();
                if (Guid.TryParse(lid, out var lg) && !string.IsNullOrWhiteSpace(rid))
                {
                    linkRows.Add((lg, rid!, ord));
                }
            }
        }

        if (linkRows.Count == 0)
            return ids.ToDictionary(x => x, _ => new List<ResourceDTO>());

        // 2) Load all involved resources from SQL once
        var allResIds = linkRows.Select(x => x.ResourceId).Distinct().ToList();
        var sqlResources = await _sqlRepository.GetResourcesByIdsAsync(allResIds);
        var resMap = sqlResources.ToDictionary(r => r.Id.ToString(), r => r, StringComparer.OrdinalIgnoreCase);

        // 3) Build per-lesson ordered DTO lists
        foreach (var group in linkRows.GroupBy(x => x.LessonId))
        {
            var list = group
                .OrderBy(g => g.OrdKey) // aligns with previous behavior
                .Select(g => resMap.TryGetValue(g.ResourceId, out var rr)
                    ? new ResourceDTO { Id = rr.Id.ToString(), Name = rr.Name, Link = rr.Link, Learned = false }
                    : new ResourceDTO { Id = g.ResourceId, Name = g.ResourceId, Link = null, Learned = false })
                .ToList();
            result[group.Key] = list;
        }
        // Ensure all requested lesson ids have an entry
        foreach (var id in ids)
        {
            result.TryAdd(id, new List<ResourceDTO>());
        }
        return result;
    }

    private async Task ReplaceLessonTagsInGraphAsync(Guid lessonVersionId, IEnumerable<Guid> tagStableIds)
    {
        var ids = tagStableIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        await using var session = _neo4jDriver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(@"
MERGE (l:Lesson {id:$lessonId})
OPTIONAL MATCH (t:Tag)-[r:TAGGED_WITH]->(l)
DELETE r", new { lessonId = lessonVersionId.ToString() });

            if (ids.Count == 0)
            {
                return;
            }

            await tx.RunAsync(@"
MATCH (l:Lesson {id:$lessonId})
UNWIND $tagIds AS tid
MERGE (t:Tag {stableId: tid})
MERGE (t)-[:TAGGED_WITH]->(l)", new { lessonId = lessonVersionId.ToString(), tagIds = ids.Select(x => x.ToString()).ToList() });
        });
    }

    private async Task<List<Guid>> GetLessonTagStableIdsFromGraphAsync(Guid lessonVersionId)
    {
        var map = await GetLessonTagStableIdsByLessonIdsAsync(new[] { lessonVersionId });
        return map.TryGetValue(lessonVersionId, out var ids) ? ids : new List<Guid>();
    }

    private async Task<Dictionary<Guid, List<Guid>>> GetLessonTagStableIdsByLessonIdsAsync(IEnumerable<Guid> lessonVersionIds)
    {
        var ids = lessonVersionIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        var result = ids.ToDictionary(id => id, _ => new List<Guid>());
        if (ids.Count == 0)
        {
            return result;
        }

        await using var session = _neo4jDriver.AsyncSession();
        var records = await session.ExecuteReadAsync(async tx =>
        {
            var cypher = @"
UNWIND $lessonIds AS lid
OPTIONAL MATCH (l:Lesson {id: lid})
OPTIONAL MATCH (t:Tag)-[:TAGGED_WITH]->(l)
RETURN lid AS lessonId, collect(DISTINCT coalesce(t.stableId, t.id)) AS tagIds";
            var cursor = await tx.RunAsync(cypher, new { lessonIds = ids.Select(x => x.ToString()).ToList() });
            return await cursor.ToListAsync();
        });

        foreach (var rec in records)
        {
            var lessonIdStr = rec["lessonId"].As<string?>();
            if (!Guid.TryParse(lessonIdStr, out var lessonId))
            {
                continue;
            }

            var tagList = new List<Guid>();
            foreach (var raw in rec["tagIds"].As<List<object>>())
            {
                var s = raw?.ToString();
                if (Guid.TryParse(s, out var gid))
                {
                    tagList.Add(gid);
                }
            }

            result[lessonId] = tagList.Distinct().ToList();
        }

        return result;
    }

    private async Task<Dictionary<Guid, string>> BuildTagNameMapAsync(IEnumerable<Guid> tagStableIds)
    {
        if (_tagRepo == null) return new Dictionary<Guid, string>();
        var ids = tagStableIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        return await _tagRepo.GetTagNamesAsync(ids);
    }

    private static void AddLessonByType(StudyPlanDetail detail, string stepType, Lesson lessonDto)
    {
        switch (stepType)
        {
            case StudyPlanStepTypes.Prerequisite:
                detail.Prerequisite.Add(lessonDto);
                break;
            case StudyPlanStepTypes.AdvancedTopic:
                detail.AdvancedTopics.Add(lessonDto);
                break;
            default:
                detail.MainCurriculum.Add(lessonDto);
                break;
        }
    }

    private static Sciencetopia.DTOs.StudyPlanLockfile? TryParseLockfile(string? json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            return System.Text.Json.JsonSerializer.Deserialize<Sciencetopia.DTOs.StudyPlanLockfile>(json);
        }
        catch { return null; }
    }

    private async Task<List<ResourceDTO>> LoadLessonResourcesAsync(Guid lessonVersionId)
    {
        // 1) Read resource ids from graph to preserve lesson-resource associations and ordering
        List<string> resourceIds;
        await using (var session = _neo4jDriver.AsyncSession())
        {
            resourceIds = await session.ExecuteReadAsync(async tx =>
            {
                var cursor = await tx.RunAsync(@"
MATCH (l:Lesson {id:$lessonId})-[:HAS_RESOURCE]->(r:Resource)
RETURN r.id AS id, coalesce(r.order, r.name, r.title) AS ord
ORDER BY ord", new { lessonId = lessonVersionId.ToString() });

                var ids = new List<string>();
                await cursor.ForEachAsync(record =>
                {
                    var id = record["id"].As<string?>();
                    if (!string.IsNullOrWhiteSpace(id)) ids.Add(id!);
                });
                return ids;
            });
        }

        if (resourceIds.Count == 0) return new List<ResourceDTO>();

        // 2) Load name/link from SQL by ids (authoritative store)
        var sqlResources = await _sqlRepository.GetResourcesByIdsAsync(resourceIds);
        var map = sqlResources.ToDictionary(r => r.Id.ToString(), r => r, StringComparer.OrdinalIgnoreCase);

        // 3) Build DTOs in graph order; if not found in SQL, include id-only entry
        var list = new List<ResourceDTO>(resourceIds.Count);
        foreach (var rid in resourceIds)
        {
            if (map.TryGetValue(rid, out var res))
            {
                list.Add(new ResourceDTO { Id = res.Id.ToString(), Name = res.Name, Link = res.Link, Learned = false });
            }
            else
            {
                list.Add(new ResourceDTO { Id = rid, Name = rid, Link = null, Learned = false });
            }
        }
        return list;
    }

    private async Task<List<TagDTO>> BuildTagDtosAsync(IEnumerable<Guid> tagStableIds)
    {
        if (_tagRepo == null)
        {
            return new List<TagDTO>();
        }

        var ids = tagStableIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        if (ids.Count == 0)
        {
            return new List<TagDTO>();
        }

        var nameMap = await _tagRepo.GetTagNamesAsync(ids);
        return ids.Select(id => new TagDTO
        {
            Id = id,
            Name = nameMap.TryGetValue(id, out var name) ? name : null
        }).ToList();
    }

    private async Task<List<Guid>> ResolveTagIdsAsync(IEnumerable<Guid> tagIds)
    {
        if (_tagRepo == null)
        {
            return new List<Guid>();
        }

        var ids = tagIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        if (ids.Count == 0)
        {
            return new List<Guid>();
        }

        // Map incoming IDs (which may be version IDs) to stable IDs
        var details = await _tagRepo.GetTagDetailsAsync(ids);
        return details.Keys.ToList();
    }

    private async Task<List<Guid>> ResolveTagNamesToStableIdsAsync(IEnumerable<string> names, string submittedBy)
    {
        if (_tagResolution == null)
        {
            return new List<Guid>();
        }

        var clean = names?
            .Select(n => (n ?? string.Empty).Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        if (clean.Count == 0)
        {
            return new List<Guid>();
        }

        var (resolved, _) = await _tagResolution.ResolveOrCreateAsync(clean, submittedBy);
        return await ResolveTagIdsAsync(resolved);
    }

    private IEnumerable<string> ExtractKeywords(params string?[] sources)
    {
        static bool IsNoise(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return true;
            if (token.Length <= 2) return true;
            var lowered = token.ToLowerInvariant();
            return StopWords.Contains(lowered);
        }

        var tokens = new List<string>();
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            var parts = Regex.Split(source, @"[^\p{L}\p{N}]+", RegexOptions.Compiled)
                .Where(p => !IsNoise(p))
                .Select(p => p.ToLowerInvariant());
            tokens.AddRange(parts);
        }

        return tokens.Distinct().Take(20);
    }

    private async Task<List<TagDTO>> SuggestTagsFromKeywordsAsync(IEnumerable<string> keywords, int take)
    {
        if (_tagRepo == null)
        {
            return new List<TagDTO>();
    }

        var suggestions = new List<TagDTO>();
        var seen = new HashSet<Guid>();

        foreach (var keyword in keywords)
        {
            var matches = await _tagRepo.SearchTagsAsync(keyword);
            foreach (var match in matches)
            {
                if (match?.Id == null || match.Id == Guid.Empty) continue;
                if (seen.Add(match.Id.Value))
                {
                    suggestions.Add(match);
                }
                if (suggestions.Count >= take) break;
            }
            if (suggestions.Count >= take) break;
        }

        return suggestions;
    }

    private async Task<TagSuggestionBundle> SuggestTagsFromDetailAsync(StudyPlanDetail detail)
    {
        if (_tagRepo == null)
        {
            return new TagSuggestionBundle(new List<TagDTO>(), new List<LessonTagSuggestion>());
        }

        var planKeywords = ExtractKeywords(detail.Title, detail.Introduction?.Description);
        var planSuggestions = await SuggestTagsFromKeywordsAsync(planKeywords, 10);

        var lessons = new List<LessonTagSuggestion>();

        async Task CollectLessonsAsync(IEnumerable<Lesson>? list, string section)
        {
            if (list == null) return;
            var idx = 0;
            foreach (var lesson in list)
            {
                var lessonKeywords = ExtractKeywords(lesson?.Name, lesson?.Description);
                var tags = await SuggestTagsFromKeywordsAsync(lessonKeywords, 10);
                var lessonId = lesson?.Id;
                var key = lessonId ?? $"{section}:{idx}:{lesson?.Name}";
                lessons.Add(new LessonTagSuggestion(lessonId, key, tags));
                idx++;
            }
        }

        await CollectLessonsAsync(detail.Prerequisite, "prerequisite");
        await CollectLessonsAsync(detail.MainCurriculum, "mainCurriculum");
        await CollectLessonsAsync(detail.AdvancedTopics, "advancedTopics");

        return new TagSuggestionBundle(planSuggestions, lessons);
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "from", "that", "this", "have", "will", "your", "about", "into", "there", "their", "what", "when", "where", "which", "shall", "would", "could", "should"
    };

    private sealed record LessonTagSuggestion(string? LessonId, string Key, List<TagDTO> Tags);
    private sealed record TagSuggestionBundle(List<TagDTO> PlanTags, List<LessonTagSuggestion> Lessons);
}
