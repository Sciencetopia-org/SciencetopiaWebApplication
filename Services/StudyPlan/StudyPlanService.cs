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

            var lessonStableId = lessonEntity.StableId == Guid.Empty ? lessonEntity.Id : lessonEntity.StableId;
            var tagIds = await ResolveTagNamesToStableIdsAsync(names, userId);
            await _sqlRepository.ReplaceLessonTagAssignmentsAsync(lessonStableId, lessonEntity.VersionNumber, tagIds);
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
            Status = "Current",
            IsCurrent = true,
            PublishedAt = utcOffsetNow,
            CreatedAt = utcOffsetNow,
            CreatedBy = userId,
            ApprovedAt = utcOffsetNow,
            ApprovedBy = userId,
            Title = planDto.Title,
            Description = planDto.Introduction?.Description,
            CreatorId = Guid.Parse(userId),
            CreatedDate = utcNow,
            UpdatedDate = utcNow
        };

        await _sqlRepository.InsertStudyPlanAsync(planEntity);

        var snapshots = await UpdateLessonsAsync(planStableId, planEntity.VersionNumber, planDto, createNewVersion: false, userId, utcOffsetNow);
        await _sqlRepository.ReplacePlanLessonSnapshotsAsync(planStableId, planEntity.VersionNumber, snapshots);

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

    private async Task<List<StudyPlanLessonSnapshot>> UpdateLessonsAsync(Guid planStableId, int planVersionNumber, StudyPlanDetail? studyPlan, bool createNewVersion, string userId, DateTimeOffset timestamp)
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
                    Status = "Current",
                    IsCurrent = true,
                    PublishedAt = timestamp,
                    ApprovedAt = timestamp,
                    ApprovedBy = userId,
                    CreatedAt = timestamp,
                    CreatedBy = userId,
                    Title = lesson.Name,
                    Description = lesson.Description,
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
                        Status = "Current",
                        IsCurrent = true,
                        PublishedAt = timestamp,
                        ApprovedAt = timestamp,
                        ApprovedBy = userId,
                        CreatedAt = timestamp,
                        CreatedBy = userId,
                        Title = lesson.Name,
                        Description = lesson.Description,
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
                Status = "Current",
                IsCurrent = true,
                PublishedAt = offsetNow,
                ApprovedAt = offsetNow,
                ApprovedBy = userId,
                CreatedAt = offsetNow,
                CreatedBy = existingPlan.CreatedBy ?? userId,
                Title = updatedStudyPlan.StudyPlan.Title ?? existingPlan.Title,
                Description = updatedStudyPlan.StudyPlan.Introduction?.Description ?? existingPlan.Description,
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
            existingPlan.UpdatedDate = utcNow;
            existingPlan.CreatedAt ??= offsetNow;
            existingPlan.CreatedBy ??= userId;
            targetVersion = existingPlan;
            await _sqlRepository.UpdateStudyPlanAsync(targetVersion);
        }

        var lessonSnapshots = await UpdateLessonsAsync(stableId, targetVersion.VersionNumber, updatedStudyPlan.StudyPlan, createNewVersion, userId, offsetNow);
        await _sqlRepository.ReplacePlanLessonSnapshotsAsync(stableId, targetVersion.VersionNumber, lessonSnapshots);
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
            await using var session = _neo4jDriver.AsyncSession();
            await session.ExecuteWriteAsync(async tx =>
            {
                await tx.RunAsync(@"MATCH (p:StudyPlan {id:$id}) DETACH DELETE p", new { id = target.Id.ToString() });
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

        var stableId = lesson.StableId == Guid.Empty ? lesson.Id : lesson.StableId;
        var tagStableIds = await _sqlRepository.GetLessonTagStableIdsAsync(stableId, lesson.VersionNumber);
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

        var stableId = lesson.StableId == Guid.Empty ? lesson.Id : lesson.StableId;
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

        await _sqlRepository.ReplaceLessonTagAssignmentsAsync(stableId, lesson.VersionNumber, finalStableIds);
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

        var lessonStable = lessonEntity.StableId == Guid.Empty ? lessonEntity.Id : lessonEntity.StableId;
        var lessonTags = await _sqlRepository.GetLessonTagStableIdsAsync(lessonStable, lessonEntity.VersionNumber);
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

        var snapshots = await _sqlRepository.GetPlanLessonSnapshotsAsync(stableId, entity.VersionNumber);
        foreach (var snapshot in snapshots.OrderBy(s => s.StepOrder))
        {
            var lessonEntity = await _sqlRepository.GetLessonByStableIdAsync(snapshot.LessonStableId, snapshot.LessonVersionNumber);
            if (lessonEntity == null)
            {
                continue;
            }

            var lessonDto = MapLessonEntityToDto(lessonEntity);
            lessonDto.Resources = await LoadLessonResourcesAsync(lessonEntity.Id);
            var lessonTagStableIds = await _sqlRepository.GetLessonTagStableIdsAsync(snapshot.LessonStableId, snapshot.LessonVersionNumber);
            lessonDto.Tags = await BuildTagDtosAsync(lessonTagStableIds);

            switch (snapshot.StepType)
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

        var planTags = await _sqlRepository.GetPlanTagStableIdsAsync(stableId, entity.VersionNumber);
        detail.Tags = await BuildTagDtosAsync(planTags);

        return detail;
    }

    private async Task<List<ResourceDTO>> LoadLessonResourcesAsync(Guid lessonVersionId)
    {
        var resources = new List<ResourceDTO>();

        await using var session = _neo4jDriver.AsyncSession();
        var rows = await session.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(@"
MATCH (l:Lesson {id:$lessonId})-[:HAS_RESOURCE]->(r:Resource)
RETURN r.id AS id, r.name AS name, r.link AS link
ORDER BY coalesce(r.order, r.name)", new { lessonId = lessonVersionId.ToString() });

            var result = new List<(string? Id, string? Name, string? Link)>();
            await cursor.ForEachAsync(record =>
            {
                result.Add((record["id"].As<string?>(), record["name"].As<string?>(), record["link"].As<string?>()));
            });
            return result;
        });

        foreach (var (id, name, link) in rows)
        {
            resources.Add(new ResourceDTO
            {
                Id = id,
                Name = name,
                Link = link,
                Learned = false
            });
        }

        return resources;
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
