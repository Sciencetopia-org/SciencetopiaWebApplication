using System.Collections;
using System.Linq;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;
using Sciencetopia.Data;
using Sciencetopia.Models;
using Sciencetopia.DTOs.KnowledgeGraphDTOs;
using Sciencetopia.Services.KnowledgeGraph;

public class KnowledgeGraphService
{
    private static readonly TimeSpan GraphCacheTtl = TimeSpan.FromMinutes(10);
    private readonly IDriver _driver;
    private readonly ApplicationDbContext _context;
    private readonly IGraphRepository _graphRepository;
    private readonly IKnowledgeNodeRepository _knowledgeRepo;
    private readonly ITagRepository _tagRepo;
    private readonly IResourceRepository _resourceRepo;
    private readonly Sciencetopia.Services.L10n.IL10nService _l10n;
    private readonly Microsoft.Extensions.Options.IOptions<Sciencetopia.Services.L10n.L10nOptions> _l10nOptions;
    private readonly IKnowledgeGraphWorkflowService _workflow;
    private readonly IDraftFreezeService _draftFreeze;
    private readonly IMemoryCache _cache;
    private readonly ILogger<KnowledgeGraphService> _logger;

    public KnowledgeGraphService(
        IDriver driver,
        ApplicationDbContext context,
        IGraphRepository graphRepository,
        IKnowledgeNodeRepository knowledgeRepo,
        ITagRepository tagRepo,
        IResourceRepository resourceRepo,
        Sciencetopia.Services.L10n.IL10nService l10n,
        Microsoft.Extensions.Options.IOptions<Sciencetopia.Services.L10n.L10nOptions> l10nOptions,
        IKnowledgeGraphWorkflowService workflow,
        IDraftFreezeService draftFreeze,
        IMemoryCache cache,
        ILogger<KnowledgeGraphService> logger)
    {
        _driver = driver;
        _context = context;
        _graphRepository = graphRepository;
        _knowledgeRepo = knowledgeRepo;
        _tagRepo = tagRepo;
        _resourceRepo = resourceRepo;
        _l10n = l10n;
        _l10nOptions = l10nOptions;
        _workflow = workflow;
        _draftFreeze = draftFreeze;
        _cache = cache;
        _logger = logger;
    }


    public async Task<object> GetKnowledgeGraphAsync(string tagSystem, string viewType, string userId, string language = "zh", bool includePending = false)
    {
        var cacheKey = $"kg:all:{tagSystem}:{viewType}:{language}";
        var data = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = GraphCacheTtl;
            var allTagIds = await GetCachedTagIdsByTagTypeAsync(tagSystem);

            return viewType.ToLower() switch
            {
                "venn" => await GetVennGraphDataAsync(allTagIds, language),
                _ => await GetNetworkGraphDataAsync(allTagIds, language)
            };
        });

        if (includePending && !string.IsNullOrEmpty(userId))
        {
            var dataPending = await GetPendingNodesByUserIdAsync(userId);
            return new { data = UnwrapGraphPayload(data), data_pending = dataPending };
        }

        return data!;
    }

    public async Task<KnowledgeNodeDetailDTO?> GetNodeDetailsByIdAsync(Guid nodeId, string language = "zh")
    {
        var details = await _knowledgeRepo.GetNodeDetailsByIdAsync(nodeId, language);
        if (!details.HasValue) return null;
        var d = details.Value;
        if (_l10nOptions.Value.Enabled)
        {
            var title = await _l10n.GetLocalizedAsync(nodeId, "name", language);
            var desc = await _l10n.GetLocalizedAsync(nodeId, "description", language);
            if (!string.IsNullOrWhiteSpace(title)) d = (title!, d.Description, d.CreatedDate, d.UpdatedDate);
            if (!string.IsNullOrWhiteSpace(desc)) d = (d.Name, desc!, d.CreatedDate, d.UpdatedDate);
        }
        return new KnowledgeNodeDetailDTO
        {
            Id = nodeId,
            Name = d.Name,
            Description = d.Description,
            CreatedDate = d.CreatedDate,
            UpdatedDate = d.UpdatedDate
        };
    }

    public async Task<object> GetKnowledgeGraphInViewAsync(string tagSystem, string viewType, IEnumerable<string> zoomLevels, string userId, string language = "zh", bool includePending = false)
    {
        var zoomLevelList = (zoomLevels ?? Enumerable.Empty<string>()).Distinct().ToList();
        var zoomKey = string.Join(",", zoomLevelList.OrderBy(x => x, StringComparer.Ordinal));
        var cacheKey = $"kg:view:{tagSystem}:{viewType}:{language}:{zoomKey}";
        var totalSw = Stopwatch.StartNew();
        var cacheHit = _cache.TryGetValue<object>(cacheKey, out var cachedData);
        if (cacheHit)
        {
            _logger.LogInformation(
                "KG GetNodeInView cache hit. tagSystem={TagSystem} viewType={ViewType} zoomLevels={ZoomLevels} lang={Lang} elapsedMs={ElapsedMs}",
                tagSystem,
                viewType,
                zoomKey,
                language,
                totalSw.ElapsedMilliseconds);
        }

        var data = cacheHit
            ? cachedData
            : await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = GraphCacheTtl;
            var missSw = Stopwatch.StartNew();

            var tagSw = Stopwatch.StartNew();
            var allTagIds = await GetCachedTagIdsByTagTypeAsync(tagSystem);
            tagSw.Stop();

            _logger.LogInformation(
                "KG GetNodeInView cache miss. tagSystem={TagSystem} viewType={ViewType} zoomLevels={ZoomLevels} lang={Lang} tagCount={TagCount} tagFetchMs={TagFetchMs}",
                tagSystem,
                viewType,
                zoomKey,
                language,
                allTagIds.Count(),
                tagSw.ElapsedMilliseconds);

            var graphData = viewType.ToLower() switch
            {
                "venn" => await GetVennGraphDataInViewAsync(allTagIds, zoomLevelList, language),
                _ => await GetNetworkGraphDataInViewAsync(allTagIds, zoomLevelList, language)
            };

            missSw.Stop();
            _logger.LogInformation(
                "KG GetNodeInView cache miss completed. tagSystem={TagSystem} viewType={ViewType} zoomLevels={ZoomLevels} lang={Lang} elapsedMs={ElapsedMs}",
                tagSystem,
                viewType,
                zoomKey,
                language,
                missSw.ElapsedMilliseconds);

            return graphData;
        });

        if (includePending && !string.IsNullOrEmpty(userId))
        {
            var pendingSw = Stopwatch.StartNew();
            var dataPending = await GetPendingNodesByUserIdAsync(userId);
            pendingSw.Stop();
            totalSw.Stop();
            _logger.LogInformation(
                "KG GetNodeInView includePending. tagSystem={TagSystem} viewType={ViewType} zoomLevels={ZoomLevels} lang={Lang} pendingCount={PendingCount} pendingMs={PendingMs} totalMs={TotalMs}",
                tagSystem,
                viewType,
                zoomKey,
                language,
                dataPending.Count,
                pendingSw.ElapsedMilliseconds,
                totalSw.ElapsedMilliseconds);
            return new { data = UnwrapGraphPayload(data), data_pending = dataPending };
        }

        totalSw.Stop();
        _logger.LogInformation(
            "KG GetNodeInView completed. tagSystem={TagSystem} viewType={ViewType} zoomLevels={ZoomLevels} lang={Lang} totalMs={TotalMs}",
            tagSystem,
            viewType,
            zoomKey,
            language,
            totalSw.ElapsedMilliseconds);

        return data!;
    }

    private async Task<object> GetNetworkGraphDataAsync(IEnumerable<Guid> tagIds, string language)
    {
        var nodeTagTriples = await _graphRepository.GetNodeTagTriplesRelatedToTagsAsync(tagIds);
        var data = await BuildKnowledgeGraphDataFromTriplesAsync(nodeTagTriples, language);

        return new { data };
    }

    private async Task<object> GetNetworkGraphDataInViewAsync(IEnumerable<Guid> tagIds, IEnumerable<string> zoomLevels, string language)
    {
        var totalSw = Stopwatch.StartNew();
        var zoomLevelList = (zoomLevels ?? Enumerable.Empty<string>()).Distinct().ToList();

        var tripleSw = Stopwatch.StartNew();
        var nodeTagTriples = await _graphRepository.GetAllNodesRelatedToTagsInViewAsync(tagIds, zoomLevelList);
        tripleSw.Stop();

        var distinctNodeCount = nodeTagTriples.Select(x => x.NodeId).Distinct().Count();
        var distinctTagCount = nodeTagTriples.Select(x => x.TagId).Distinct().Count();
        _logger.LogInformation(
            "KG GetNodeInView triples fetched. zoomLevels={ZoomLevels} inputTagCount={InputTagCount} filteredTagCount={FilteredTagCount} tagFilterMs={TagFilterMs} tripleCount={TripleCount} nodeCount={NodeCount} tagCount={TagCount} triplesMs={TriplesMs}",
            string.Join(",", zoomLevelList),
            tagIds?.Distinct().Count() ?? 0,
            distinctTagCount,
            0,
            nodeTagTriples.Count,
            distinctNodeCount,
            distinctTagCount,
            tripleSw.ElapsedMilliseconds);

        var buildSw = Stopwatch.StartNew();
        var data = await BuildKnowledgeGraphDataFromTriplesAsync(nodeTagTriples, language);
        buildSw.Stop();
        totalSw.Stop();

        _logger.LogInformation(
            "KG GetNodeInView network graph built. zoomLevels={ZoomLevels} nodes={NodeCount} links={LinkCount} buildMs={BuildMs} totalMs={TotalMs}",
            string.Join(",", zoomLevelList),
            data.Nodes?.Count ?? 0,
            data.Links?.Count ?? 0,
            buildSw.ElapsedMilliseconds,
            totalSw.ElapsedMilliseconds);

        return new { data };
    }

    private async Task<object> GetVennGraphDataAsync(IEnumerable<Guid> tagIds, string language)
    {
        // 获取所有标签→节点映射（不传递）
        var allVennGroups = await _graphRepository.GetVennTagNodeGroupsAsync();

        // 只保留用户请求的标签
        var vennGroups = allVennGroups
            .Where(g => tagIds.Contains(g.TagId))
            .ToList();

        // 获取包含关系（原始所有关系）
        var allContainRelations = await _graphRepository.GetPureTagContainRelationsAsync(tagIds);

        // 只保留两端都在当前标签体系中的包含关系
        var validContainRelations = allContainRelations
            .Where(r => tagIds.Contains(r.ParentTagId) && tagIds.Contains(r.ChildTagId))
            .Select(r => (r.ParentTagId, r.ChildTagId))
            .ToList();

        // 收集所有涉及的标签和节点 ID（为了查 SQL 元信息）
        var involvedTagIds = vennGroups.Select(g => g.TagId)
            .Union(validContainRelations.Select(r => r.ParentTagId))
            .Union(validContainRelations.Select(r => r.ChildTagId))
            .Distinct()
            .ToList();

        var involvedNodeIds = vennGroups
            .SelectMany(g => g.NodeIds)
            .Distinct()
            .ToList();

        // 获取标签名称（从 SQL）
        var tagMetaDict = await _tagRepo.GetTagNamesAsync(involvedTagIds, language);
        // var tagMetaDict = await _tagRepo.GetTagDetailsAsync(involvedTagIds);

        // 获取节点名称（从 SQL）
        var nodeMetaDict = _l10nOptions.Value.Enabled
            ? await GetNodeTitlesAsync(involvedNodeIds, language)
            : await _knowledgeRepo.GetNodesNamesAsync(involvedNodeIds, language);
        // var nodeMetaDict = await _knowledgeRepo.GetNodesDetailsAsync(involvedNodeIds);

        // 构造 Venn 集合部分（sets）
        var sets = vennGroups.Select(g => new
        {
            id = g.TagId,
            name = tagMetaDict.TryGetValue(g.TagId, out var meta) ? meta : g.TagId.ToString(),
            // name = tagMetaDict.TryGetValue(g.TagId, out var meta) ? meta.Name : g.TagId.ToString(),
            elements = g.NodeIds.Select(nodeId => new
            {
                id = nodeId,
                name = nodeMetaDict.TryGetValue(nodeId, out var nmeta) ? nmeta : nodeId.ToString()
                // name = nodeMetaDict.TryGetValue(nodeId, out var nmeta) ? nmeta.Name : nodeId.ToString()
            })
        });

        // 构造标签间的包含关系
        var relations = validContainRelations.Select(r => new
        {
            parent = new
            {
                id = r.ParentTagId,
                name = tagMetaDict.TryGetValue(r.ParentTagId, out var parentMeta) ? parentMeta : r.ParentTagId.ToString()
                // name = tagMetaDict.TryGetValue(r.Parent, out var parentMeta) ? parentMeta.Name : r.Parent.ToString()
            },
            child = new
            {
                id = r.ChildTagId,
                name = tagMetaDict.TryGetValue(r.ChildTagId, out var childMeta) ? childMeta : r.ChildTagId.ToString()
                // name = tagMetaDict.TryGetValue(r.Child, out var childMeta) ? childMeta.Name : r.Child.ToString()
            }
        });

        return new { venn = new { sets, contain_relations = relations } };
    }

    private async Task<object> GetVennGraphDataInViewAsync(IEnumerable<Guid> tagIds, IEnumerable<string> zoomLevels, string language)
    {
        // 记录请求的标签集合，便于后续查找
        var tagGuidSet = tagIds.ToHashSet();

        // 获取所有标签→节点映射（不传递）
        var allVennGroups = await _graphRepository.GetVennTagNodeGroupsInViewAsync(zoomLevels);

        // 只保留用户请求的标签
        var vennGroups = allVennGroups
            .Where(g => tagGuidSet.Contains(g.TagId))
            .ToList();

        // 获取包含关系（原始所有关系）
        var allContainRelations = await _graphRepository.GetPureTagContainRelationsAsync(tagIds);

        // 只保留两端都在当前标签体系中的包含关系
        var containRelations = allContainRelations
            .Where(r => tagGuidSet.Contains(r.ParentTagId) && tagGuidSet.Contains(r.ChildTagId))
            .Select(r => new
            {
                Parent = r.ParentTagId,
                Child = r.ChildTagId
            })
            .ToList();

        // 收集所有涉及的标签和节点 ID（为了查 SQL 元信息）
        var involvedTagIds = vennGroups.Select(g => g.TagId)
            .Concat(containRelations.Select(r => r.Parent))
            .Concat(containRelations.Select(r => r.Child))
            .Distinct()
            .ToList();

        var involvedNodeIds = vennGroups
            .SelectMany(g => g.NodeIds)
            .Distinct()
            .ToList();

        // 获取标签名称（从 SQL）
        var tagMetaDict = await _tagRepo.GetTagNamesAsync(involvedTagIds, language);
        // var tagMetaDict = await _tagRepo.GetTagDetailsAsync(involvedTagIds);

        // 获取节点名称（从 SQL）
        var nodeMetaDict = _l10nOptions.Value.Enabled
            ? await GetNodeTitlesAsync(involvedNodeIds, language)
            : await _knowledgeRepo.GetNodesNamesAsync(involvedNodeIds);
        // var nodeMetaDict = await _knowledgeRepo.GetNodesDetailsAsync(involvedNodeIds);

        // 构造 Venn 集合部分（sets）
        var sets = vennGroups.Select(g => new
        {
            id = g.TagId,
            name = tagMetaDict.TryGetValue(g.TagId, out var meta) ? meta : g.TagId.ToString(),
            // name = tagMetaDict.TryGetValue(g.TagId, out var meta) ? meta.Name : g.TagId.ToString(),
            elements = g.NodeIds.Select(nodeId => new
            {
                id = nodeId,
                name = nodeMetaDict.TryGetValue(nodeId, out var nmeta) ? nmeta : nodeId.ToString()
                // name = nodeMetaDict.TryGetValue(nodeId, out var nmeta) ? nmeta.Name : nodeId.ToString()
            })
        });

        // 构造标签间的包含关系
        var relations = containRelations.Select(r => new
        {
            parent = new
            {
                id = r.Parent,
                name = tagMetaDict.TryGetValue(r.Parent, out var parentMeta) ? parentMeta : r.Parent.ToString()
                // name = tagMetaDict.TryGetValue(r.Parent, out var parentMeta) ? parentMeta.Name : r.Parent.ToString()
            },
            child = new
            {
                id = r.Child,
                name = tagMetaDict.TryGetValue(r.Child, out var childMeta) ? childMeta : r.Child.ToString()
                // name = tagMetaDict.TryGetValue(r.Child, out var childMeta) ? childMeta.Name : r.Child.ToString()
            }
        });

        return new { venn = new { sets, contain_relations = relations } };
    }

    public async Task<GraphDTO> GetAdjacentNodesByLevelAsync(IEnumerable<string> parentIds, string zoomLevel)
    {
        var parentGuids = parentIds.Select(Guid.Parse).ToList();
        var parentChildLinks = await _graphRepository.GetAdjacentNodeLinksByLevelAsync(parentGuids, zoomLevel);
        var childNodeIds = parentChildLinks
            .Select(link => link.ChildId)
            .Distinct()
            .ToList();

        if (childNodeIds.Count == 0)
        {
            return new GraphDTO
            {
                Nodes = new List<NodeDTO>(),
                Links = new List<LinkDTO>()
            };
        }

        var childNames = await _knowledgeRepo.GetNodesNamesAsync(childNodeIds);

        var nodes = childNodeIds
            .Select(nodeId => new NodeDTO
            {
                Id = nodeId,
                Name = childNames.TryGetValue(nodeId, out var name) ? name : nodeId.ToString(),
                TagLevel = zoomLevel
            })
            .ToList();

        var links = parentChildLinks
            .Select(link => new LinkDTO
            {
                Source = link.ParentId,
                Target = link.ChildId,
                Relation = "CONTAIN"
            })
            .GroupBy(link => new { link.Source, link.Target, link.Relation })
            .Select(group => group.First())
            .ToList();

        return new GraphDTO
        {
            Nodes = nodes,
            Links = links
        };
    }

    public async Task<IEnumerable<Guid>> GetAllKnowledgeNodeIdsAsync()
    {
        // 从 SQL 获取所有节点 id
        return await _knowledgeRepo.GetAllNodeIdsAsync();
    }

    public async Task<IEnumerable<Guid>> GetTagIdsByTagTypeAsync(string tagType)
    {
        // 从 SQL 获取指定标签类型的节点 id
        var tagIdStrings = await _tagRepo.GetTagNodeIdsByTagTypeAsync(tagType);
        return tagIdStrings;
    }

    public async Task<IEnumerable<Guid>> GetTagIdsByTagTypeAmongNodesAsync(IEnumerable<Guid> nodeIds, string tagType)
    {
        // 从 SQL 获取指定节点 id 中有的指定类型的标签 id
        // 1. 从 SQL 查标签
        var tagIds = await _tagRepo.GetTagNodeIdsByTagTypeAsync(tagType);
        if (!tagIds.Any())
        {
            // 没有任何符合条件的标签，直接返回空列表
            return new List<Guid>();
        }

        // 2. 去 Neo4j 查找 TAGGED_WITH 这些知识节点的标签
        var relatedTypeIds = await _graphRepository.GetAllTagsRelatedToNodesAsync(nodeIds);

        // 3. 返回两个结果的交集
        return tagIds.Intersect(relatedTypeIds);
    }

    public async Task<IEnumerable<Guid>> GetAllNodesRelatedToTags(IEnumerable<Guid> tagIds)
    {
        return await _graphRepository.GetAllNodesRelatedToTagsAsync(tagIds);
    }

    public async Task<GraphDTO> GetKnowledgeGraphDataByNodeId(IEnumerable<Guid> allNodeIds,
    IEnumerable<Guid> allTagIds,
    IReadOnlyDictionary<Guid, string>? nodeToLevel = null,
    string language = "zh")
    {
        // 标签与节点的本地化已在各仓储层处理，无需在此额外覆盖
        // 使用 SQL 显式表获取代表节点详情（TagId -> NodeId + meta）
        var repDetails = await _tagRepo.GetRepresentativeNodesAsync(allTagIds, language);
        var repMap = repDetails.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.NodeId);
        var repNodeIds = repMap.Values.Distinct().ToList();

        // 扩充节点集：原始节点 + 代表节点
        var nodeIdsToBuild = allNodeIds.Union(repNodeIds).Distinct().ToList();

        // 统一获取节点层级：优先复用传入 map，并补齐代表节点
        Dictionary<Guid, string> levelsMap;
        if (nodeToLevel is not null && nodeToLevel.Count > 0)
        {
            levelsMap = new Dictionary<Guid, string>(nodeToLevel);
            var missing = nodeIdsToBuild.Where(id => !levelsMap.ContainsKey(id)).ToList();
            if (missing.Count > 0)
            {
                var fetched = await _graphRepository.GetNodeLevelsByNodeIdsAsync(missing);
                foreach (var kv in fetched)
                    levelsMap[kv.Key] = kv.Value;
            }
        }
        else
        {
            levelsMap = await _graphRepository.GetNodeLevelsByNodeIdsAsync(nodeIdsToBuild);
        }

        // 获取节点标题（仓储内已 L10n 兼容）
        var nodesDict = await _knowledgeRepo.GetNodesNamesAsync(nodeIdsToBuild, language);

        // 转换成 NodeDTO 列表
        var sqlNodes = nodesDict.Select(kvp =>
        {
            var nodeId = kvp.Key;
            var name = kvp.Value;
            return new NodeDTO
            {
                Id = nodeId,
                Name = name,
                TagLevel = levelsMap.TryGetValue(nodeId, out var lvl) ? lvl : "Keyword"
            };
        }).ToList();

        // 旧的 TagLevel 反查逻辑已废弃，统一使用 levelsMap

        // 获取所有关系
        var taggedRelations = await _graphRepository.GetTaggedRelationsAsync(allNodeIds, allTagIds);
        var projectedRelations = new List<ProjectedRelationDTO>(); // 仍为空
        var tagContainRelations = await _graphRepository.GetPureTagContainRelationsAsync(allTagIds);

        // 基于代表节点直接构建标签层次（CONTAIN）在知识图中的边
        var hierarchyRelations = new List<HierarchyRelationDTO>();
        foreach (var relation in tagContainRelations)
        {
            if (repMap.TryGetValue(relation.ParentTagId, out var parentNodeId) &&
                repMap.TryGetValue(relation.ChildTagId, out var childNodeId))
            {
                hierarchyRelations.Add(new HierarchyRelationDTO
                {
                    ParentId = parentNodeId,
                    ChildId = childNodeId
                });
            }
        }

        // 将 TAGGED_WITH 替换为 代表节点 -> 实际知识节点 的边（也用 CONTAIN 命名以适配前端）
        var replacedTaggedRelations = taggedRelations
            .Where(t => repMap.ContainsKey(t.TagId))
            .Select(t => new HierarchyRelationDTO
            {
                ParentId = repMap[t.TagId],
                ChildId = t.SourceId
            })
            .ToList();

        // 构造 links
        var linksDto = new List<LinkDTO>();
        linksDto.AddRange(hierarchyRelations.Select(r => new LinkDTO
        {
            Source = r.ParentId,
            Target = r.ChildId,
            Relation = "CONTAIN"
        }));
        linksDto.AddRange(projectedRelations.Select(r => new LinkDTO
        {
            Source = r.SourceId,
            Target = r.TargetId,
            Relation = "projected",
            Weight = r.Weight
        }));
        linksDto.AddRange(replacedTaggedRelations.Select(r => new LinkDTO
        {
            Source = r.ParentId,
            Target = r.ChildId,
            Relation = "CONTAIN"
        }));

        return new GraphDTO
        {
            Nodes = sqlNodes,
            Links = linksDto
        };
    }

    private async Task<GraphDTO> BuildKnowledgeGraphDataFromTriplesAsync(
        IEnumerable<(Guid NodeId, Guid TagId, string TagLevel)> nodeTagTriples,
        string language)
    {
        var totalSw = Stopwatch.StartNew();
        var triples = (nodeTagTriples ?? Enumerable.Empty<(Guid NodeId, Guid TagId, string TagLevel)>()).ToList();
        if (triples.Count == 0)
        {
            _logger.LogInformation("KG graph build skipped because triples are empty.");
            return new GraphDTO
            {
                Nodes = new List<NodeDTO>(),
                Links = new List<LinkDTO>()
            };
        }

        var allNodeIds = triples.Select(x => x.NodeId).Distinct().ToList();
        var allTagIds = triples.Select(x => x.TagId).Distinct().ToList();
        var nodeToLevel = triples
            .GroupBy(x => x.NodeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.TagLevel).First());

        var repMeasured = await MeasureAsync(() => GetCachedRepresentativeNodeIdsAsync(allTagIds));
        var repMap = repMeasured.Result;
        var repNodeIds = repMap.Values.Distinct().ToList();
        var nodeIdsToBuild = allNodeIds.Union(repNodeIds).Distinct().ToList();

        var levelsMap = new Dictionary<Guid, string>(nodeToLevel);
        var missingLevels = nodeIdsToBuild.Where(id => !levelsMap.ContainsKey(id)).ToList();
        var levelsSw = Stopwatch.StartNew();
        if (missingLevels.Count > 0)
        {
            var fetched = await _graphRepository.GetNodeLevelsByNodeIdsAsync(missingLevels);
            foreach (var kv in fetched)
            {
                levelsMap[kv.Key] = kv.Value;
            }
        }
        levelsSw.Stop();

        var namesTask = MeasureAsync(() => GetCachedCurrentNodeNamesAsync(nodeIdsToBuild, language));
        var containTask = MeasureAsync(() => GetCachedTagContainRelationsAsync(allTagIds));
        await Task.WhenAll(namesTask, containTask);
        var nodesDict = namesTask.Result.Result;
        var sqlNodes = nodesDict.Select(kvp => new NodeDTO
        {
            Id = kvp.Key,
            Name = kvp.Value,
            TagLevel = levelsMap.TryGetValue(kvp.Key, out var lvl) ? lvl : "Keyword"
        }).ToList();

        var tagContainRelations = containTask.Result.Result;

        var linksSw = Stopwatch.StartNew();
        var linksDto = new List<LinkDTO>();

        linksDto.AddRange(tagContainRelations
            .Where(relation => repMap.ContainsKey(relation.ParentTagId) && repMap.ContainsKey(relation.ChildTagId))
            .Select(relation => new LinkDTO
            {
                Source = repMap[relation.ParentTagId],
                Target = repMap[relation.ChildTagId],
                Relation = "CONTAIN"
            }));

        linksDto.AddRange(triples
            .Where(triple => repMap.ContainsKey(triple.TagId))
            .Select(triple => new LinkDTO
            {
                Source = repMap[triple.TagId],
                Target = triple.NodeId,
                Relation = "CONTAIN"
            }));

        var dedupedLinks = linksDto
            .GroupBy(link => new { link.Source, link.Target, link.Relation })
            .Select(group => group.First())
            .ToList();
        linksSw.Stop();
        totalSw.Stop();
        _logger.LogInformation(
            "KG graph build segments. triples={TripleCount} sourceNodes={SourceNodeCount} sourceTags={SourceTagCount} representativeTags={RepresentativeTagCount} representativeFetchMs={RepresentativeFetchMs} levelFillMs={LevelFillMs} nodeNameMs={NodeNameMs} containMs={ContainMs} linkAssembleMs={LinkAssembleMs} builtNodes={BuiltNodeCount} builtLinks={BuiltLinkCount} totalMs={TotalMs}",
            triples.Count,
            allNodeIds.Count,
            allTagIds.Count,
            repMap.Count,
            repMeasured.ElapsedMs,
            levelsSw.ElapsedMilliseconds,
            namesTask.Result.ElapsedMs,
            containTask.Result.ElapsedMs,
            linksSw.ElapsedMilliseconds,
            sqlNodes.Count,
            dedupedLinks.Count,
            totalSw.ElapsedMilliseconds);

        return new GraphDTO
        {
            Nodes = sqlNodes,
            Links = dedupedLinks
        };
    }

    private Task<Dictionary<Guid, Guid>> GetCachedRepresentativeNodeIdsAsync(IReadOnlyCollection<Guid> tagIds)
    {
        if (tagIds.Count == 0)
        {
            return Task.FromResult(new Dictionary<Guid, Guid>());
        }

        var cacheKey = BuildGuidSetCacheKey("kg:repmap", tagIds);
        return _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await _tagRepo.GetRepresentativeNodeIdsAsync(tagIds);
        })!;
    }

    private Task<Dictionary<Guid, string>> GetCachedCurrentNodeNamesAsync(IReadOnlyCollection<Guid> stableIds, string language)
    {
        if (stableIds.Count == 0)
        {
            return Task.FromResult(new Dictionary<Guid, string>());
        }

        var cacheKey = BuildGuidSetCacheKey($"kg:nodenames:{language}", stableIds);
        return _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            return await _knowledgeRepo.GetCurrentNodeNamesByStableIdsAsync(stableIds, language);
        })!;
    }

    private Task<List<TagContainRelationDTO>> GetCachedTagContainRelationsAsync(IReadOnlyCollection<Guid> tagIds)
    {
        if (tagIds.Count == 0)
        {
            return Task.FromResult(new List<TagContainRelationDTO>());
        }

        var cacheKey = BuildGuidSetCacheKey("kg:contain", tagIds);
        return _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await _graphRepository.GetPureTagContainRelationsAsync(tagIds);
        })!;
    }

    private Task<List<Guid>> GetCachedTagIdsByTagTypeAsync(string tagSystem)
    {
        var cacheKey = $"kg:tagsystem:{tagSystem}";
        return _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return (await _tagRepo.GetTagNodeIdsByTagTypeAsync(tagSystem)).Distinct().ToList();
        })!;
    }

    private static string BuildGuidSetCacheKey(string prefix, IEnumerable<Guid> ids)
    {
        var joined = string.Join(",", ids.OrderBy(id => id).Select(id => id.ToString("N")));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
        return $"{prefix}:{hash}";
    }

    private static async Task<(T Result, long ElapsedMs)> MeasureAsync<T>(Func<Task<T>> work)
    {
        var sw = Stopwatch.StartNew();
        var result = await work();
        sw.Stop();
        return (result, sw.ElapsedMilliseconds);
    }

    // Helpers

    public async Task<IEnumerable<string>> GetTagIdsByTagNamesAsync(IEnumerable<string> inputTagNames)
    {
        var tags = await _tagRepo.GetTagsByNameAsync(inputTagNames);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tags)
        {
            if (t?.Name != null && t.Id.HasValue)
                map[t.Name] = t.Id.Value.ToString();
        }
        return inputTagNames.Select(name => map.TryGetValue(name ?? string.Empty, out var id) ? id : string.Empty);
    }

    public async Task<IEnumerable<Guid>> GetNodeIdsByTagsAsync(IEnumerable<string> inputTagIds)
    {
        // Use only self + immediate children tag ids per input tag, and intersect across inputs
        HashSet<Guid>? intersection = null;

        foreach (var tagId in (inputTagIds ?? Enumerable.Empty<string>()))
        {
            var scopeTagIds = await _graphRepository.GetSelfAndImmediateChildrenTagIdsAsync(new List<string> { tagId });
            if (intersection == null)
                intersection = new HashSet<Guid>(scopeTagIds);
            else
                intersection.IntersectWith(scopeTagIds);

            if (intersection.Count == 0)
                return Enumerable.Empty<Guid>();
        }

        if (intersection != null && intersection.Count > 0)
        {
            var nodeIds = await _graphRepository.GetAllNodesRelatedToTagsAsync(intersection);
            return nodeIds;
        }

        return Enumerable.Empty<Guid>();
    }
    // 统一使用带语言参数的节点详情方法

    public async Task<object> SearchNodeAsync(string query)
    {
        int skip = 0; // Define the starting point for pagination
        int take = 10; // Define the number of results to retrieve
        return await _knowledgeRepo.SearchKnowledgeNodesAsync(query.ToLower(), skip, take);
    }

    public async Task<List<TagDTO>> SearchTagsAsync(string query)
    {
        return await _tagRepo.SearchTagsAsync(query);
    }

    public async Task<string> CreateNodeAsync(CreateNodeRequest request, string userId)
    {
        _draftFreeze.EnsureDraftingEnabled();
        return await CreateNodeWithResourcesAsync(request, userId);
    }

    public async Task<string> CreateTagDraftAsync(string name, string? description, string userId)
    {
        _draftFreeze.EnsureDraftingEnabled();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tag name is required.", nameof(name));

        var draft = await _workflow.CreateTagDraftAsync(name, description, userId);
        await _graphRepository.CreatePendingTagNodeAsync(draft.StableId.ToString());
        return draft.StableId.ToString();
    }

    // public async Task<bool> CreateRelationshipAsync(string sourceNodeName, string targetNodeName, string relationshipType, string userId)
    // {
    //     if (sourceNodeName == null || targetNodeName == null || relationshipType == null)
    //         throw new ArgumentNullException("Source node name, target node name, and relationship type are all required.");

    //     using var session = _driver.AsyncSession();

    //     // Construct the query dynamically with the relationship type
    //     var query = $@"
    //     MATCH (source), (target)
    //     WHERE source.name = $sourceNodeName AND target.name = $targetNodeName
    //     CREATE (source)-[:{relationshipType} {{status: 'pending_approval', contributor: $userId}}]->(target)
    //     RETURN source, target";

    //     var result = await session.RunAsync(query, new { sourceNodeName, targetNodeName, userId });

    //     return await result.FetchAsync(); // True if the operation was successful
    // }

    public async Task<bool> ApproveNodeAsync(Guid versionId, string reviewerId)
    {
        await _workflow.PublishNodeAsync(versionId, reviewerId);

        var node = await _context.KnowledgeNodes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == versionId);

        if (node is { StableId: var stableIdGuid } && stableIdGuid != Guid.Empty)
        {
            var stableId = stableIdGuid.ToString();
            await _graphRepository.ApproveNodeResourceRelationsAsync(stableId);
            await _graphRepository.PromoteTagRelationsAsync(stableId);
        }
        return true;
    }

    public async Task<bool> DisapproveNodeAsync(Guid versionId, string reviewerId)
    {
        await _workflow.RejectNodeAsync(versionId, reviewerId);

        var node = await _context.KnowledgeNodes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == versionId);

        if (node is { StableId: var stableIdGuid } && stableIdGuid != Guid.Empty)
        {
            await _graphRepository.MarkTagRelationsRejectedAsync(stableIdGuid.ToString());
        }
        return true;
    }

    public async Task<bool> ApproveTagAsync(Guid versionId, string reviewerId)
    {
        await _workflow.PublishTagAsync(versionId, reviewerId);

        var tag = await _context.Tags
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == versionId);

        if (tag is { StableId: var stableIdGuid } && stableIdGuid != Guid.Empty)
        {
            await _graphRepository.SetTagStatusApprovedAsync(stableIdGuid.ToString());
        }

        return true;
    }

    public async Task<bool> DisapproveTagAsync(Guid versionId, string reviewerId)
    {
        await _workflow.RejectTagAsync(versionId, reviewerId);

        var tag = await _context.Tags
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == versionId);

        if (tag is { StableId: var stableIdGuid } && stableIdGuid != Guid.Empty)
        {
            await _graphRepository.SetTagStatusRejectedAsync(stableIdGuid.ToString());
        }

        return true;
    }

    public async Task DisapprovePendingTagsRelatedToNodeAsync(Guid nodeId)
    {
        var tagIds = await _graphRepository.GetTagsRelatedToNodeAsync(nodeId);

        foreach (var tagId in tagIds)
        {
            var pendingTagVersions = await _context.Tags
                .Where(t => t.StableId == tagId && t.Status == "Draft")
                .ToListAsync();

            if (pendingTagVersions.Count > 0)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var tagVersion in pendingTagVersions)
                {
                    tagVersion.Status = "Rejected";
                    tagVersion.IsCurrent = false;
                    tagVersion.RetiredAt = now;
                    tagVersion.ApprovedAt = now;
                }
                await _graphRepository.SetTagStatusRejectedAsync(tagId.ToString());
            }
        }

        await _context.SaveChangesAsync();
    }

    public async Task<bool> ResubmitNodeAsync(Guid versionId, string userId)
    {
        var nodeVersion = await _context.KnowledgeNodes
            .FirstOrDefaultAsync(x => x.Id == versionId);

        if (nodeVersion == null)
        {
            return false;
        }

        if (!string.Equals(nodeVersion.Status, "Rejected", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        nodeVersion.Status = "Draft";
        nodeVersion.IsCurrent = false;
        nodeVersion.CreatedAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            nodeVersion.CreatedBy = userId;
        }
        nodeVersion.PublishedAt = null;
        nodeVersion.ApprovedAt = null;
        nodeVersion.ApprovedBy = null;
        nodeVersion.RetiredAt = null;

        await _context.SaveChangesAsync();
        return true;
    }

    // public async Task<bool> ApproveRelationshipAsync(string sourceNodeName, string targetNodeName, string relationshipType)
    // {
    //     if (string.IsNullOrWhiteSpace(sourceNodeName) || string.IsNullOrWhiteSpace(targetNodeName) || string.IsNullOrWhiteSpace(relationshipType))
    //         throw new ArgumentException("Source node name, target node name, and relationship type are all required.");

    //     using var session = _driver.AsyncSession();
    //     var result = await session.RunAsync(@"
    //         MATCH (source)-[r:$relationshipType]->(target)
    //         WHERE source.name = $sourceNodeName AND target.name = $targetNodeName AND EXISTS(r.pending_approval)
    //         REMOVE r.pending_approval
    //         RETURN source, target",
    //         new { sourceNodeName, targetNodeName, relationshipType });

    //     return await result.FetchAsync(); // True if the operation was successful
    // }

    // public async Task<bool> DisapproveRelationshipAsync(string sourceNodeName, string targetNodeName, string relationshipType)
    // {
    //     if (string.IsNullOrWhiteSpace(sourceNodeName) || string.IsNullOrWhiteSpace(targetNodeName) || string.IsNullOrWhiteSpace(relationshipType))
    //         throw new ArgumentException("Source node name, target node name, and relationship type are all required.");

    //     using var session = _driver.AsyncSession();
    //     var result = await session.RunAsync(@"
    //         MATCH (source)-[r:$relationshipType]->(target)
    //         WHERE source.name = $sourceNodeName AND target.name = $targetNodeName AND EXISTS(r.pending_approval)
    //         REMOVE r.pending_approval
    //         SET r:disapproved
    //         RETURN source, target",
    //         new { sourceNodeName, targetNodeName, relationshipType });

    //     return await result.FetchAsync(); // True if the operation was successful
    // }

    // public async Task<bool> ResubmitRelationshipAsync(string sourceNodeName, string targetNodeName, string relationshipType)
    // {
    //     if (string.IsNullOrWhiteSpace(sourceNodeName) || string.IsNullOrWhiteSpace(targetNodeName) || string.IsNullOrWhiteSpace(relationshipType))
    //         throw new ArgumentException("Source node name, target node name, and relationship type are all required.");

    //     using var session = _driver.AsyncSession();
    //     var result = await session.RunAsync(@"
    //         MATCH (source)-[r:$relationshipType]->(target)
    //         WHERE source.name = $sourceNodeName AND target.name = $targetNodeName AND EXISTS(r.disapproved)
    //         REMOVE r.disapproved
    //         SET r:pending_approval
    //         RETURN source, target",
    //         new { sourceNodeName, targetNodeName, relationshipType });

    //     return await result.FetchAsync(); // True if the operation was successful
    // }

    public async Task<bool> AddResourceAsync(string nodeName, string link, string resourceName = "")
    {
        _draftFreeze.EnsureDraftingEnabled();
        return await AddResourceToNodeAsync(nodeName, link, resourceName);
    }

    public Task<IReadOnlyList<PendingNodeSummary>> GetPendingNodesAsync()
        => _workflow.GetPendingNodeDraftsAsync();

    public async Task<IReadOnlyList<PendingNodeSummary>> GetPendingNodesByUserIdAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Array.Empty<PendingNodeSummary>();
        }

        return await _context.KnowledgeNodes
            .AsNoTracking()
            .Where(x => x.Status == "Draft" && !x.IsCurrent && x.CreatedBy == userId)
            .OrderByDescending(x => x.CreatedAt ?? DateTimeOffset.MinValue)
            .Select(x => new PendingNodeSummary(
                x.StableId,
                x.Id!.Value,
                x.Name ?? string.Empty,
                x.Description,
                x.CreatedAt,
                x.CreatedBy
            ))
            .ToListAsync();
    }

    public Task<IReadOnlyList<PendingTagSummary>> GetPendingTagsAsync()
        => _workflow.GetPendingTagDraftsAsync();

    public async Task<IReadOnlyList<PendingTagSummary>> GetPendingTagsByUserIdAsync(string userId)
    {
        var drafts = await _workflow.GetPendingTagDraftsAsync();
        return drafts.Where(x => string.Equals(x.SubmittedBy, userId, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static object? UnwrapGraphPayload(object? payload)
    {
        if (payload == null)
        {
            return null;
        }

        var prop = payload.GetType().GetProperty("data");
        return prop?.GetValue(payload) ?? payload;
    }

    public async Task<List<int>> CountContributedNodesAndLinks(string userId)
    {
        return await CountContributedNodesAndLinksAsync(userId);
    }

    public async Task<List<int>> CountContributedNodesAndLinksAsync(string userId)
    {
        var approvedNodes = await _context.KnowledgeNodes
            .Where(x => x.CreatedBy == userId && x.Status == "Current")
            .Select(x => x.StableId)
            .Distinct()
            .CountAsync();
        var approvedLinks = await _graphRepository.CountApprovedLinksByUserAsync(userId);
        return new List<int> { approvedNodes, approvedLinks };
    }

    public async Task<string> CreateNodeWithResourcesAsync(CreateNodeRequest request, string userId)
    {
        // Step 1: 创建 SQL 草稿
        var draft = await _workflow.CreateNodeDraftAsync(request, userId);
        await _graphRepository.CreateNodeAndResourceInGraphAsync(
            draft.StableId.ToString(),
            request.Name,
            request.Description,
            request.Link,
            userId
        );

        await HandleTagRelationsAsync(
            draft.StableId,
            request.TagIds,
            request.NewTagNames,
            userId
        );

        return draft.VersionId.ToString();
    }

    public async Task<bool> EditNodeAsync(EditNodeRequest request, string userId)
    {
        _draftFreeze.EnsureDraftingEnabled();
        // Step 1: 创建 SQL 草稿（已模块化）
        var draft = await _workflow.CreateNodeEditDraftAsync(request, userId);

        await HandleTagRelationsAsync(
            draft.StableId,
            request.TagIds,
            request.TagNames,
            userId
        );

        return true;
    }

    private async Task HandleTagRelationsAsync(
    Guid nodeId,
    List<Guid>? tagIds,
    List<string>? newTagNames,
    string userId)
    {
        // Step 1: 已有标签 → 建立 TAGGED_WITH
        if (tagIds != null && tagIds.Any())
        {
            foreach (var tagId in tagIds.Distinct())
            {
                await _graphRepository.RelateTagToNodeAsync(tagId.ToString(), nodeId.ToString());
            }
        }

        // Step 2: 新标签 → 创建草稿 + Neo4j 草稿节点 + 建立关系
        if (newTagNames != null && newTagNames.Any())
        {
            foreach (var tagName in newTagNames.Distinct())
            {
                var tagDraft = await _workflow.CreateTagDraftAsync(tagName, null, userId);
                await _graphRepository.CreatePendingTagNodeAsync(tagDraft.StableId.ToString());
                await _graphRepository.RelateTagToNodeAsync(tagDraft.StableId.ToString(), nodeId.ToString());
            }
        }
    }

    public async Task<bool> AddResourceToNodeAsync(string nodeId, string link, string resourceName)
    {
        if (!Guid.TryParse(nodeId, out _))
            throw new ArgumentException("Invalid node identifier.", nameof(nodeId));

        var resourceId = await _resourceRepo.EnsureResourceExistsAsync(resourceName, link);
        return await _graphRepository.LinkResourceToNodeAsync(nodeId, resourceId, null);
    }

    private async Task<Dictionary<Guid, string>> GetNodeTitlesAsync(IEnumerable<Guid> nodeIds, string language)
    {
        var result = new Dictionary<Guid, string>();
        foreach (var id in nodeIds.Distinct())
        {
            var title = await _l10n.GetLocalizedAsync(id, "name", language) ?? string.Empty;
            result[id] = string.IsNullOrWhiteSpace(title) ? id.ToString() : title;
        }
        return result;
    }
}


