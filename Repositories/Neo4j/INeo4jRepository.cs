using Neo4j.Driver;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public interface IGraphRepository
{
    /// <summary>
    /// 获取所有知识节点与纯标签节点之间的 TAGGED_WITH 关系
    /// </summary>
    Task<List<TaggedRelationDTO>> GetTaggedRelationsAsync(IEnumerable<Guid> nodeIds, IEnumerable<Guid> tagIds);
    Task<IEnumerable<Guid>> GetTagIdsInViewAsync(IEnumerable<string> zoomLevels, IEnumerable<Guid> allTagIds);

    /// <summary>
    /// 获取知识节点之间的层次关系（知识层次关系），
    /// 即标签知识节点之间按照标签体系建立的父→子关系
    /// </summary>
    Task<List<TagContainRelationDTO>> GetPureTagContainRelationsAsync(IEnumerable<Guid> allTagIds);

    /// <summary>
    /// 利用纯标签节点和 TAGGED_WITH 关系进行二部图投影，
    /// 获取共享同一标签的知识节点之间的投影边，累加权重
    /// </summary>
    Task<List<ProjectedRelationDTO>> GetProjectedRelationsAsync();

    /// <summary>
    /// 获取知识节点与标签层次节点之间的关系
    /// </summary>
    Task<Dictionary<Guid, string>> GetNodeTagLevelRelationsAsync(IEnumerable<Guid> nodeIds);

    /// <summary>
    /// 获取所有与指定标签相关的知识节点
    /// </summary>
    Task<HashSet<Guid>> GetAllNodesRelatedToTagsAsync(IEnumerable<Guid> tagIds);

    Task<Dictionary<Guid, string>> GetNodeLevelsByNodeIdsAsync(IEnumerable<Guid> nodeIds);
    Task<HashSet<(Guid NodeId, Guid TagId, string TagLevel)>> GetNodeTagTriplesRelatedToTagsAsync(IEnumerable<Guid> tagIds);
    Task<HashSet<(Guid NodeId, Guid TagId, string TagLevel)>> GetAllNodesRelatedToTagsInViewAsync(IEnumerable<Guid> tagIds, IEnumerable<string> zoomLevels);
    /// <summary>
    /// 获取所有与指定知识节点相关的标签
    /// </summary>
    Task<HashSet<Guid>> GetAllTagsRelatedToNodesAsync(IEnumerable<Guid> nodeIds);
    Task<HashSet<Guid>> GetTagsRelatedToNodeAsync(Guid nodeId);
    Task<HashSet<(Guid TagId, string ResourceId)>> GetTagsAndResourcesIdsRelatedToNodeAsync(string nodeId);
    /// <summary>
    /// 根据标签类型获取标签节点的 Id
    /// </summary>
    Task<IEnumerable<Guid>> GetNodeIdsByLabelAsync(string label);
    Task<List<AdjacentNodeLinkDTO>> GetAdjacentNodeLinksByLevelAsync(IEnumerable<Guid> parentNodeIds, string targetLevel);
    /// <summary>
    /// 获取所有与指定标签相关的知识节点
    /// </summary>
    Task<HashSet<Guid>> GetAllDescendantTagIdsAsync(IEnumerable<string> tagNames);
    // Self + immediate children only (one hop). Inputs are tag stableIds as strings.
    Task<HashSet<Guid>> GetSelfAndImmediateChildrenTagIdsAsync(IEnumerable<string> tagIds);

    /// <summary>
    /// 获取 Venn 图中标签与知识节点的分组
    /// </summary>
    Task<List<TagNodeGroup>> GetVennTagNodeGroupsAsync();
    Task<List<TagNodeGroup>> GetVennTagNodeGroupsInViewAsync(IEnumerable<string> zoomLevels);
    Task<List<string>> GetLinkedResourceIdsAsync(IEnumerable<Guid> knowledgeNodeIds);
    Task<List<string>> GetResourcesIdsRelatedToNodeAsync(string nodeId);
    Task CreateNodeAndResourceInGraphAsync(string nodeId, string name, string description, IEnumerable<string> links, string userId);
    Task<int> CountApprovedLinksByUserAsync(string userId);
    Task<bool> LinkResourceToNodeAsync(string nodeStableId, string resourceId, string? userId = null);
    Task CreateTagNodeIfNotExistsAsync(string tagId);
    Task RelateTagToNodeAsync(string tagId, string nodeId);
    Task CreatePendingTagNodeAsync(string tagId);
    Task SetTagStatusApprovedAsync(string tagId);
    Task SetTagStatusRejectedAsync(string tagId);
    Task PromoteTagRelationsAsync(string nodeId);
    Task MarkTagRelationsRejectedAsync(string nodeId);
    Task DetachResourcesFromNodeAsync(string nodeId);
    Task ApproveNodeResourceRelationsAsync(string nodeId);
    Task<Dictionary<Guid, int>> GetTagCountsForNodesAsync(IEnumerable<Guid> nodeIds);

    // Graph view methods (status/time-window filtered edges)
    Task<List<(Guid TagId, Guid NodeId)>> GetActiveGraphAsync(DateTimeOffset? asOf = null);
    Task<List<(Guid TagId, Guid NodeId)>> GetProposalsAsync();
    Task<List<(Guid TagId, Guid NodeId)>> GetAsOfAsync(DateTimeOffset asOf);
}

public class GraphRepository : IGraphRepository
{
    private readonly IDriver _driver;

    public GraphRepository(IDriver driver)
    {
        _driver = driver;
    }

    public async Task<List<TaggedRelationDTO>> GetTaggedRelationsAsync(IEnumerable<Guid> nodeIds, IEnumerable<Guid> tagIds)
    {
        using var session = _driver.AsyncSession();
        var cypher = @"
            MATCH (t:Tag)-[:TAGGED_WITH]->(n:KnowledgeNode)
            WHERE n.stableId IN $nodeIds AND t.stableId IN $tagIds
            RETURN n.stableId AS SourceId, t.stableId AS TagId
        ";
        var parameters = new Dictionary<string, object>
        {
            { "nodeIds", nodeIds.Select(id => id.ToString()) },
            { "tagIds", tagIds.Select(id => id.ToString()) }
        };
        var result = await session.RunAsync(cypher, parameters);
        return (await result.ToListAsync())
            .Select(record => new TaggedRelationDTO
            {
                SourceId = Guid.Parse(record["SourceId"].As<string>()),
                TagId = Guid.Parse(record["TagId"].As<string>())
            }).ToList();
    }

    public async Task<IEnumerable<Guid>> GetTagIdsInViewAsync(IEnumerable<string> zoomLevels, IEnumerable<Guid> allTagIds)
    {
        var tagIdArray = (allTagIds ?? Enumerable.Empty<Guid>()).Distinct().Select(id => id.ToString()).ToArray();
        var zoomLevelArray = (zoomLevels ?? Enumerable.Empty<string>()).Distinct().ToArray();
        if (tagIdArray.Length == 0 || zoomLevelArray.Length == 0) return Enumerable.Empty<Guid>();

        using var session = _driver.AsyncSession();
        var cypher = @"
    UNWIND $tagIds AS id
    MATCH (t:Tag {stableId: id})
    WHERE EXISTS {
        MATCH (t)-[:TAGGED_WITH]->(:KnowledgeNode)<-[:TAGGED_WITH]-(l:TagLevel)
        WHERE l.name IN $zoomLevels
    }
    RETURN id AS TagId;
    ";
        var result = await session.RunAsync(cypher, new { zoomLevels = zoomLevelArray, tagIds = tagIdArray });
        var records = await result.ToListAsync();

        return records.Select(r => Guid.Parse(r["TagId"].As<string>()));
    }

    // Repositories/GraphRepository.cs
    public async Task<List<TagContainRelationDTO>> GetPureTagContainRelationsAsync(IEnumerable<Guid> allTagIds)
    {
        var tagIdList = (allTagIds ?? Enumerable.Empty<Guid>()).Distinct().Select(id => id.ToString()).ToArray();
        if (tagIdList.Length == 0)
        {
            return new List<TagContainRelationDTO>();
        }

        using var session = _driver.AsyncSession();
        var cypher = @"
        UNWIND $tagIds AS parentId
        MATCH (parent:Tag {stableId: parentId})-[:CONTAIN]->(child:Tag)
        WHERE child.stableId IN $tagIds
          AND parent.stableId <> child.stableId
        RETURN parent.stableId AS ParentTagId, child.stableId AS ChildTagId
    ";
        var result = await session.RunAsync(cypher, new Dictionary<string, object>
        {
            { "tagIds", tagIdList }
        });

        return (await result.ToListAsync())
            .Select(record => new TagContainRelationDTO
            {
                ParentTagId = Guid.Parse(record["ParentTagId"].As<string>()),
                ChildTagId = Guid.Parse(record["ChildTagId"].As<string>())
            }).ToList();
    }

    public async Task<List<ProjectedRelationDTO>> GetProjectedRelationsAsync()
    {
        using var session = _driver.AsyncSession();
        // 基于纯标签节点和 TAGGED_WITH 关系投影出知识节点间共享的关系
        var cypher = @"
            MATCH (n:KnowledgeNode)<-[:TAGGED_WITH]-(t:Tag)-[:TAGGED_WITH]->(m:KnowledgeNode)
            WHERE n <> m
            WITH n, m, count(*) AS weight
            RETURN n.stableId AS SourceId, m.stableId AS TargetId, weight
        ";
        var result = await session.RunAsync(cypher);
        return (await result.ToListAsync())
            .Select(record => new ProjectedRelationDTO
            {
                SourceId = Guid.Parse(record["SourceId"].As<string>()),
                TargetId = Guid.Parse(record["TargetId"].As<string>()),
                Weight = record["weight"].As<int>()
            }).ToList();
    }

    public async Task<Dictionary<Guid, string>> GetNodeTagLevelRelationsAsync(IEnumerable<Guid> nodeIds)
    {
        using var session = _driver.AsyncSession();

        var query = @"
        MATCH (tagLevel:TagLevel)-[:TAGGED_WITH]->(node:KnowledgeNode)
        WHERE node.stableId IN $nodeIds
        RETURN node.stableId AS NodeId, tagLevel.id AS TagLevelId
        ";
        var parameters = new Dictionary<string, object>
        {
            { "nodeIds", nodeIds.Select(id => id.ToString()) }
        };

        var result = await session.RunAsync(query, parameters);

        var nodeTagLevels = new Dictionary<Guid, string>();

        await result.ForEachAsync(record =>
        {
            var nodeId = Guid.Parse(record["NodeId"].As<string>());
            var tagLevelId = record["TagLevelId"].As<string>();
            nodeTagLevels[nodeId] = tagLevelId;
        });

        return nodeTagLevels;
    }

    public async Task<HashSet<Guid>> GetAllNodesRelatedToTagsAsync(IEnumerable<Guid> tagIds)
    {
        using var session = _driver.AsyncSession();
        var query = @"
        MATCH (t:Tag)-[:TAGGED_WITH]->(n:KnowledgeNode)
        WHERE t.stableId IN $tagIds AND coalesce(n.status, 'Current') = 'Current'
        RETURN n.stableId AS NodeId
        ";
        var parameters = new Dictionary<string, object>
        {
            { "tagIds", tagIds.Select(id => id.ToString()) }
        };

        var result = await session.RunAsync(query, parameters);

        var records = await result.ToListAsync();

        return records.Select(record => Guid.Parse(record["NodeId"].As<string>())).ToHashSet();
    }

    public async Task<Dictionary<Guid, string>> GetNodeLevelsByNodeIdsAsync(IEnumerable<Guid> nodeIds)
    {
        if (nodeIds == null) return new();

        using var session = _driver.AsyncSession();
        var cypher = @"
        UNWIND $nodeIds AS nid
        MATCH (n:KnowledgeNode {stableId: nid})<-[:TAGGED_WITH]-(l:TagLevel)
        RETURN nid AS NodeId, l.name AS TagLevel
    ";
        var cursor = await session.RunAsync(cypher, new { nodeIds = nodeIds.Select(id => id.ToString()) });
        var records = await cursor.ToListAsync();

        // 若一个节点匹配多个层级，可在这里自定义优先级（示例：取第一个）
        return records
            .GroupBy(r => r["NodeId"].As<string>())
            .ToDictionary(
                g => Guid.Parse(g.Key),
                g => g.Select(x => x["TagLevel"].As<string>()).First()
            );
    }

    public async Task<HashSet<(Guid NodeId, Guid TagId, string TagLevel)>> GetNodeTagTriplesRelatedToTagsAsync(
    IEnumerable<Guid> tagIds)
    {
        using var session = _driver.AsyncSession();
        var query = @"
        UNWIND $tagIds AS id
        MATCH (t:Tag {stableId: id})-[:TAGGED_WITH]->(n:KnowledgeNode)<-[:TAGGED_WITH]-(l:TagLevel)
        WHERE coalesce(n.status, 'Current') = 'Current'
        RETURN DISTINCT n.stableId AS NodeId, id AS TagId, l.name AS TagLevel
    ";
        var result = await session.RunAsync(query, new { tagIds = tagIds.Select(id => id.ToString()) });
        var records = await result.ToListAsync();

        return records
            .Select(r => (
                NodeId: Guid.Parse(r["NodeId"].As<string>()),
                TagId: Guid.Parse(r["TagId"].As<string>()),
                TagLevel: r["TagLevel"].As<string>()
            ))
            .ToHashSet();
    }

    public async Task<HashSet<(Guid NodeId, Guid TagId, string TagLevel)>> GetAllNodesRelatedToTagsInViewAsync(
        IEnumerable<Guid> tagIds, IEnumerable<string> zoomLevels)
    {
        if (tagIds == null)
        {
            return new HashSet<(Guid, Guid, string)>();
        }

        var tagIdArray = tagIds.Distinct().ToArray();
        if (tagIdArray.Length == 0)
        {
            return new HashSet<(Guid, Guid, string)>();
        }

        var zoomLevelArray = (zoomLevels ?? Enumerable.Empty<string>()).ToArray();
        if (zoomLevelArray.Length == 0)
        {
            return new HashSet<(Guid, Guid, string)>();
        }

        var query = @"
        UNWIND $tagIds AS id
        MATCH (t:Tag {stableId: id})-[:TAGGED_WITH]->(n:KnowledgeNode)<-[:TAGGED_WITH]-(l:TagLevel)
        WHERE l.name IN $zoomLevels
          AND coalesce(n.status, 'Current') = 'Current'
        RETURN DISTINCT n.stableId AS NodeId, t.stableId AS TagId, l.name AS TagLevel
    ";

        const int chunkSize = 80;
        var chunkTasks = tagIdArray
            .Chunk(chunkSize)
            .Select(async chunk =>
            {
                using var session = _driver.AsyncSession();
                var chunkResults = new HashSet<(Guid NodeId, Guid TagId, string TagLevel)>();
                var cursor = await session.RunAsync(query, new
                {
                    tagIds = chunk.Select(id => id.ToString()).ToArray(),
                    zoomLevels = zoomLevelArray
                });

                await cursor.ForEachAsync(record =>
                {
                    var nodeId = Guid.Parse(record["NodeId"].As<string>());
                    var tagId = Guid.Parse(record["TagId"].As<string>());
                    var tagLevel = record["TagLevel"].As<string>();
                    chunkResults.Add((nodeId, tagId, tagLevel));
                });

                return chunkResults;
            })
            .ToArray();

        var chunkResults = await Task.WhenAll(chunkTasks);
        var nodeTagTriples = new HashSet<(Guid NodeId, Guid TagId, string TagLevel)>();
        foreach (var chunkResult in chunkResults)
        {
            nodeTagTriples.UnionWith(chunkResult);
        }

        return nodeTagTriples;
    }

    public async Task<HashSet<Guid>> GetTagsRelatedToNodeAsync(Guid nodeId)
    {
        using var session = _driver.AsyncSession();
        var query = @"
        MATCH (t:Tag)-[:TAGGED_WITH]->(n:KnowledgeNode)
        WHERE n.stableId = $nodeId
        RETURN t.stableId AS TagId
        ";
        var parameters = new Dictionary<string, object>
        {
            { "nodeId", nodeId.ToString() }
        };

        var result = await session.RunAsync(query, parameters);

        var records = await result.ToListAsync();
        return records.Select(record => Guid.Parse(record["TagId"].As<string>())).ToHashSet();
    }

    public async Task<HashSet<Guid>> GetAllTagsRelatedToNodesAsync(IEnumerable<Guid> nodeIds)
    {
        using var session = _driver.AsyncSession();
        var query = @"
        MATCH (t:Tag)-[:TAGGED_WITH]->(n:KnowledgeNode)
        WHERE n.stableId IN $nodeIds
        RETURN t.stableId AS TagId
        ";
        var parameters = new Dictionary<string, object>
        {
            { "nodeIds", nodeIds.Select(id => id.ToString()) }
        };

        var result = await session.RunAsync(query, parameters);

        var records = await result.ToListAsync();
        return records.Select(record => Guid.Parse(record["TagId"].As<string>())).ToHashSet();
    }

    public async Task<HashSet<(Guid TagId, string ResourceId)>> GetTagsAndResourcesIdsRelatedToNodeAsync(string nodeId)
    {
        using var session = _driver.AsyncSession();
        var query = @"
        MATCH (t:Tag)-[:TAGGED_WITH]->(n:KnowledgeNode)-[:HAS_RESOURCE]->(r:Resource)
        WHERE n.stableId = $nodeId
        RETURN t.stableId AS TagId, r.id AS ResourceId
        ";
        var parameters = new Dictionary<string, object>
        {
            { "nodeId", nodeId }
        };

        var result = await session.RunAsync(query, parameters);

        var records = await result.ToListAsync();
        return records
            .Select(record => (
                TagId: Guid.Parse(record["TagId"].As<string>()),
                ResourceId: record["ResourceId"].As<string>()
            ))
            .ToHashSet();
    }

    public async Task<IEnumerable<Guid>> GetNodeIdsByLabelAsync(string label)
    {
        // 确保 label 仅包含安全的字符（防止 Cypher 注入攻击）
        if (!Regex.IsMatch(label, "^[A-Za-z0-9_]+$"))
        {
            throw new ArgumentException("Invalid label format.");
        }

        // 直接拼接 label 到查询字符串
        string query = $@"
        MATCH (t:{label})
        RETURN t.stableId AS TagId
    ";

        using var session = _driver.AsyncSession();

        var result = await session.RunAsync(query);

        return await result.ToListAsync(record => Guid.Parse(record["TagId"].As<string>()));
    }

    public async Task<HashSet<Guid>> GetAllDescendantTagIdsAsync(IEnumerable<string> tagIds)
    {
        using var session = _driver.AsyncSession();

        if (!tagIds.Any()) return new HashSet<Guid>();

        // Include the parent tag itself (length-0 path) so direct-tagged nodes are included
        var query = @"
        UNWIND $tagIds AS pid
        MATCH (parent:Tag {stableId: pid})-[:CONTAIN*0..]->(child:Tag)
        RETURN DISTINCT child.stableId AS tagId";

        var parameters = new { tagIds };

        var result = await session.RunAsync(query, parameters);
        var records = await result.ToListAsync();
        return records.Select(record => Guid.Parse(record["tagId"].As<string>())).ToHashSet();
    }

    public async Task<HashSet<Guid>> GetSelfAndImmediateChildrenTagIdsAsync(IEnumerable<string> tagIds)
    {
        using var session = _driver.AsyncSession();

        var ids = (tagIds ?? Enumerable.Empty<string>()).Distinct().ToList();
        if (ids.Count == 0) return new HashSet<Guid>();

        // Return both the parent tag itself and its immediate children
        var query = @"
        UNWIND $tagIds AS pid
        MATCH (parent:Tag {stableId: pid})
        RETURN DISTINCT parent.stableId AS tagId
        UNION
        UNWIND $tagIds AS pid
        MATCH (parent:Tag {stableId: pid})-[:CONTAIN]->(child:Tag)
        RETURN DISTINCT child.stableId AS tagId";

        var result = await session.RunAsync(query, new { tagIds = ids });
        var records = await result.ToListAsync();
        return records.Select(r => Guid.Parse(r["tagId"].As<string>())).ToHashSet();
    }

    public async Task<List<TagNodeGroup>> GetVennTagNodeGroupsAsync()
    {
        using var session = _driver.AsyncSession();
        var cypher = @"
        MATCH (t:Tag)-[:TAGGED_WITH]->(n:KnowledgeNode)
        WHERE coalesce(n.status, 'Current') = 'Current'
        RETURN t.stableId AS TagId, collect(DISTINCT n.stableId) AS NodeIds
    ";

        var result = await session.RunAsync(cypher);
        var records = await result.ToListAsync();

        return records
            .Select(r => new TagNodeGroup
            {
                TagId = Guid.Parse(r["TagId"].As<string>()),
                NodeIds = r["NodeIds"].As<List<string>>().Select(Guid.Parse).ToList()
            })
            .ToList();
    }

    public async Task<List<TagNodeGroup>> GetVennTagNodeGroupsInViewAsync(IEnumerable<string> zoomLevels)
    {
        // 这里的 zoomLevel 参数可以用于未来的扩展，比如根据不同的缩放级别返回不同的数据
        // 目前我们直接返回所有标签和节点的关系
        // 如果需要根据 zoomLevel 过滤数据，可以在查询中添加相应的条件

        // 获取所有标签→节点映射
        {
            using var session = _driver.AsyncSession();
            var cypher = @"
        MATCH (t:Tag)-[:TAGGED_WITH]->(n:KnowledgeNode)<-[:TAGGED_WITH]-(t2:TagLevel)
        WHERE coalesce(n.status, 'Current') = 'Current'
          AND t2.name IN $zoomLevels
        RETURN t.stableId AS TagId, collect(DISTINCT n.stableId) AS NodeIds
    ";

            var result = await session.RunAsync(cypher, new { zoomLevels });
            // 获取查询结果
            var records = await result.ToListAsync();

            return records
                .Select(r => new TagNodeGroup
                {
                    TagId = Guid.Parse(r["TagId"].As<string>()),
                    NodeIds = r["NodeIds"].As<List<string>>().Select(Guid.Parse).ToList()
                })
                .ToList();
        }
    }

    public async Task<List<string>> GetLinkedResourceIdsAsync(IEnumerable<Guid> knowledgeNodeIds)
    {
        if (!knowledgeNodeIds.Any())
            return new List<string>();

        var resourceIds = new List<string>();

        var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
        UNWIND $ids AS nodeId
        MATCH (n:KnowledgeNode {stableId: nodeId})-[:HAS_RESOURCE]->(r:Resource)
        RETURN DISTINCT r.id AS resourceId", new { ids = knowledgeNodeIds.Select(id => id.ToString()) });

        await foreach (var record in result)
        {
            var id = record["resourceId"]?.As<string>();
            if (!string.IsNullOrWhiteSpace(id))
                resourceIds.Add(id);
        }

        return resourceIds;
    }

    public async Task<List<string>> GetResourcesIdsRelatedToNodeAsync(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return new List<string>();

        using var session = _driver.AsyncSession();
        var query = @"
        MATCH (n:KnowledgeNode {stableId: $nodeId})-[:HAS_RESOURCE]->(r:Resource)
        RETURN r.id AS ResourceId";

        var result = await session.RunAsync(query, new { nodeId });

        return (await result.ToListAsync())
            .Select(record => record["ResourceId"].As<string>())
            .ToList();
    }

    public async Task CreateNodeAndResourceInGraphAsync(string nodeId, string name, string description, IEnumerable<string> links, string userId)
    {
        var query = @"
        MERGE (n:KnowledgeNode {stableId: $nodeId})
        SET n.name = $name, n.description = $description
        SET n.status = 'Draft'
        WITH n
        UNWIND $links AS l
        MERGE (r:Resource {link: l})
        SET r.status = 'Draft'
        MERGE (n)-[:HAS_RESOURCE {status: 'Draft', contributor: $userId}]->(r)
        WITH n
        MERGE (u:User {id: $userId})
        MERGE (u)-[:CREATED]->(n)";

        var parameters = new
        {
            nodeId = nodeId.ToString(),
            name,
            description,
            links = links ?? new List<string>(),
            userId
        };

        using var session = _driver.AsyncSession();
        await session.RunAsync(query, parameters);
    }

    public async Task<int> CountApprovedLinksByUserAsync(string userId)
    {
        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (n)-[rel]->(m)
            WHERE rel.userId = $userId AND coalesce(rel.status, 'Current') = 'Current'
            RETURN COUNT(rel) AS linkCount
        ", new { userId });

        if (await result.FetchAsync())
        {
            return result.Current["linkCount"].As<int>();
        }

        return 0;
    }

    public async Task<bool> LinkResourceToNodeAsync(string nodeStableId, string resourceId, string? userId = null)
    {
        var query = @"
            MATCH (n:KnowledgeNode {stableId: $nodeId})
            MERGE (r:Resource {id: $resourceId})
            SET r.status = coalesce(r.status, 'Draft')
            MERGE (n)-[rel:HAS_RESOURCE]->(r)
            SET rel.status = 'Draft',
                rel.contributor = coalesce($userId, rel.contributor)
            RETURN r";

        try
        {
            using var session = _driver.AsyncSession();
            var result = await session.RunAsync(query, new { nodeId = nodeStableId, resourceId, userId });
            return await result.FetchAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Neo4j error in LinkResourceToNodeAsync: {ex.Message}");
            return false;
        }
    }

    public async Task CreateTagNodeIfNotExistsAsync(string tagId)
    {
        var query = @"MERGE (t:Tag {stableId: $tagId})";
        using var session = _driver.AsyncSession();
        var results = await session.RunAsync(query, new Dictionary<string, object> { { "tagId", tagId.ToString() } });
        await results.FetchAsync();
    }

    public async Task RelateTagToNodeAsync(string tagId, string nodeId)
    {
        var query = @"
        MATCH (t:Tag {stableId: $tagId})
        MATCH (n:KnowledgeNode {stableId: $nodeId})
        MERGE (t)-[:TAGGED_WITH {status: 'Draft'}]->(n)";

        var parameters = new Dictionary<string, object>
    {
        { "tagId", tagId.ToString() },
        { "nodeId", nodeId.ToString() }
    };

        var session = _driver.AsyncSession();

        try
        {
            await session.RunAsync(query, parameters);
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    public async Task CreatePendingTagNodeAsync(string tagId)
    {
        var query = @"
        MERGE (t:Tag {stableId: $tagId})
        SET t.status = 'Draft'";

        var parameters = new Dictionary<string, object>
    {
        { "tagId", tagId.ToString() }
    };

        var session = _driver.AsyncSession(); // _neo4jDriver 是 IDriver 实例

        try
        {
            await session.RunAsync(query, parameters);
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    public async Task SetTagStatusApprovedAsync(string tagId)
    {
        var query = @"
        MATCH (t:Tag {stableId: $tagId})
        SET t.status = 'Current'";

        var parameters = new Dictionary<string, object>
    {
        { "tagId", tagId.ToString() }
    };

        // 开启一个 Neo4j 会话（写模式）
        var session = _driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Write).WithDatabase("neo4j"));

        try
        {
            await session.RunAsync(query, parameters);
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    public async Task SetTagStatusRejectedAsync(string tagId)
    {
        var query = @"
        MATCH (t:Tag {stableId: $tagId})
        SET t.status = 'Rejected'";

        var session = _driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Write).WithDatabase("neo4j"));
        try
        {
            await session.RunAsync(query, new { tagId = tagId.ToString() });
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    public async Task PromoteTagRelationsAsync(string nodeId)
    {
        var query = @"
        MATCH (:Tag)-[r:TAGGED_WITH]->(n:KnowledgeNode {stableId: $nodeId})
        SET r.status = 'Current'";

        using var session = _driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Write).WithDatabase("neo4j"));
        await session.RunAsync(query, new { nodeId });
    }

    public async Task MarkTagRelationsRejectedAsync(string nodeId)
    {
        var query = @"
        MATCH (:Tag)-[r:TAGGED_WITH]->(n:KnowledgeNode {stableId: $nodeId})
        SET r.status = 'Rejected'";

        using var session = _driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Write).WithDatabase("neo4j"));
        await session.RunAsync(query, new { nodeId });
    }

    public async Task DetachResourcesFromNodeAsync(string nodeId)
    {
        var query = @"
        MATCH (n:KnowledgeNode {stableId: $nodeId})-[r:HAS_RESOURCE]->(:Resource)
        DELETE r";

        var session = _driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Write).WithDatabase("neo4j"));
        try
        {
            await session.RunAsync(query, new { nodeId = nodeId.ToString() });
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    public async Task ApproveNodeResourceRelationsAsync(string nodeId)
    {
        var query = @"
        MATCH (n:KnowledgeNode {stableId: $nodeId})-[r:HAS_RESOURCE]->(res:Resource)
        SET r.status = 'Current',
            res.status = 'Current'";

        var session = _driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Write).WithDatabase("neo4j"));
        try
        {
            await session.RunAsync(query, new { nodeId = nodeId.ToString() });
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    public async Task<List<AdjacentNodeLinkDTO>> GetAdjacentNodeLinksByLevelAsync(IEnumerable<Guid> parentNodeIds, string targetLevel)
    {
        using var session = _driver.AsyncSession();

        var query = @"
        MATCH (p:KnowledgeNode)<-[:TAGGED_WITH]-(pt:Tag)
        MATCH (pt)-[:CONTAIN*0..]->(ct:Tag)-[:TAGGED_WITH]->(c:KnowledgeNode)<-[:TAGGED_WITH]-(lvl:TagLevel {name: $targetLevel})
        WHERE p.stableId IN $parentIds AND coalesce(c.status, 'Current') = 'Current'
        RETURN DISTINCT p.stableId AS ParentId, c.stableId AS ChildId";

        var parameters = new Dictionary<string, object>
        {
            { "parentIds", parentNodeIds.Select(id => id.ToString()) },
            { "targetLevel", targetLevel }
        };

        var result = await session.RunAsync(query, parameters);
        var records = await result.ToListAsync();

        return records
            .Select(r => new AdjacentNodeLinkDTO(
                Guid.Parse(r["ParentId"].As<string>()),
                Guid.Parse(r["ChildId"].As<string>())))
            .Distinct()
            .ToList();
    }

    public async Task<Dictionary<Guid, int>> GetTagCountsForNodesAsync(IEnumerable<Guid> nodeIds)
    {
        var result = new Dictionary<Guid, int>();
        var ids = (nodeIds ?? Enumerable.Empty<Guid>()).Select(x => x.ToString()).Distinct().ToList();
        if (ids.Count == 0) return result;
        using var session = _driver.AsyncSession();
        var cypher = @"
UNWIND $nodeIds AS nid
MATCH (t:Tag)-[:TAGGED_WITH]->(n:KnowledgeNode {stableId: nid})
RETURN t.stableId AS tagId, count(DISTINCT n) AS cnt";
        var cursor = await session.RunAsync(cypher, new { nodeIds = ids });
        var rows = await cursor.ToListAsync();
        foreach (var r in rows)
        {
            var tagIdStr = r["tagId"].As<string?>();
            var cnt = r["cnt"].As<int>();
            if (Guid.TryParse(tagIdStr, out var gid)) result[gid] = cnt;
        }
        return result;
    }

    public async Task<List<(Guid TagId, Guid NodeId)>> GetActiveGraphAsync(DateTimeOffset? asOf = null)
    {
        var t = (asOf ?? DateTimeOffset.UtcNow).UtcDateTime;
        using var session = _driver.AsyncSession();
        var cypher = @"
MATCH (t:Tag)-[r:TAGGED_WITH]->(k:KnowledgeNode)
WHERE r.status='Current'
  AND r.validFrom <= $asOf
  AND (r.validTo IS NULL OR $asOf < r.validTo)
RETURN t.stableId AS tagId, k.stableId AS nodeId";
        var cursor = await session.RunAsync(cypher, new { asOf = t.ToString("o") });
        var rows = await cursor.ToListAsync();
        return rows.Select(r => (Guid.Parse(r["tagId"].As<string>()), Guid.Parse(r["nodeId"].As<string>()))).ToList();
    }

    public async Task<List<(Guid TagId, Guid NodeId)>> GetProposalsAsync()
    {
        using var session = _driver.AsyncSession();
        var cypher = @"
MATCH (t:Tag)-[r:TAGGED_WITH]->(k:KnowledgeNode)
WHERE r.status='proposed'
RETURN t.stableId AS tagId, k.stableId AS nodeId";
        var cursor = await session.RunAsync(cypher);
        var rows = await cursor.ToListAsync();
        return rows.Select(r => (Guid.Parse(r["tagId"].As<string>()), Guid.Parse(r["nodeId"].As<string>()))).ToList();
    }

    public Task<List<(Guid TagId, Guid NodeId)>> GetAsOfAsync(DateTimeOffset asOf)
        => GetActiveGraphAsync(asOf);
}
