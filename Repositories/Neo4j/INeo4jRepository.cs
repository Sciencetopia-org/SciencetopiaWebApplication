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
    Task<HashSet<Guid>> GetAdjacentNodesByLevelAsync(IEnumerable<Guid> parentNodeIds, string targetLevel);
    /// <summary>
    /// 获取所有与指定标签相关的知识节点
    /// </summary>
    Task<HashSet<Guid>> GetAllDescendantTagIdsAsync(IEnumerable<string> tagNames);

    /// <summary>
    /// 获取 Venn 图中标签与知识节点的分组
    /// </summary>
    Task<List<TagNodeGroup>> GetVennTagNodeGroupsAsync();
    Task<List<TagNodeGroup>> GetVennTagNodeGroupsInViewAsync(IEnumerable<string> zoomLevels);
    Task<List<string>> GetLinkedResourceIdsAsync(IEnumerable<Guid> knowledgeNodeIds);
    Task<List<string>> GetResourcesIdsRelatedToNodeAsync(string nodeId);
    Task CreateNodeAndResourceInGraphAsync(string nodeId, string name, string description, IEnumerable<string> links, string userId);
    Task<int> CountApprovedLinksByUserAsync(string userId);
    Task<bool> LinkResourceToNodeAsync(string nodeName, string resourceId);
    Task CreateTagNodeIfNotExistsAsync(string tagId);
    Task RelateTagToNodeAsync(string tagId, string nodeId);
    Task CreatePendingTagNodeAsync(string tagId);
    Task SetTagStatusApprovedAsync(string tagId);
    Task SetTagStatusRejectedAsync(string tagId);
    Task DetachResourcesFromNodeAsync(string nodeId);
    Task ApproveNodeResourceRelationsAsync(string nodeId);
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
            WHERE (n.id) IN $nodeIds AND (t.id) IN $tagIds
            RETURN n.id AS SourceId, t.id AS TagId
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
        if (allTagIds == null || !allTagIds.Any()) return Enumerable.Empty<Guid>();

        using var session = _driver.AsyncSession();
        var cypher = @"
    MATCH (t:Tag)-[:TAGGED_WITH]->(:KnowledgeNode)<-[:TAGGED_WITH]-(l:TagLevel)
    WHERE t.id IN $tagIds AND l.name IN $zoomLevels
    RETURN DISTINCT t.id AS TagId;
    ";
        var result = await session.RunAsync(cypher, new { zoomLevels, tagIds = allTagIds.Select(id => id.ToString()) });
        var records = await result.ToListAsync();

        return records.Select(r => Guid.Parse(r["TagId"].As<string>()));
    }

    // Repositories/GraphRepository.cs
    public async Task<List<TagContainRelationDTO>> GetPureTagContainRelationsAsync(IEnumerable<Guid> allTagIds)
    {
        using var session = _driver.AsyncSession();
        var cypher = @"
        MATCH (parent:Tag)-[:CONTAIN]->(child:Tag)
        WHERE parent.id IN $tagIds AND child.id IN $tagIds
        WITH parent, child
        WHERE parent.id <> child.id
        RETURN parent.id AS ParentTagId, child.id AS ChildTagId
    ";
        var parameters = new Dictionary<string, object>
        {
            { "tagIds", allTagIds.Select(id => id.ToString()) }
        };
        var result = await session.RunAsync(cypher, parameters);
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
            RETURN n.id AS SourceId, m.id AS TargetId, weight
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
        WHERE node.id IN $nodeIds
        RETURN node.id AS NodeId, tagLevel.id AS TagLevelId
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
        WHERE t.id IN $tagIds AND (n.status IS NULL OR n.status <> 'pending_approval')
        RETURN n.id AS NodeId
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
        MATCH (n:KnowledgeNode {id: nid})<-[:TAGGED_WITH]-(l:TagLevel)
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
        MATCH (t:Tag {id: id})-[:TAGGED_WITH]->(n:KnowledgeNode)<-[:TAGGED_WITH]-(l:TagLevel)
        WHERE (n.status IS NULL OR n.status <> 'pending_approval')
        RETURN DISTINCT n.id AS NodeId, id AS TagId, l.name AS TagLevel
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
        using var session = _driver.AsyncSession();
        var query = @"
        MATCH (t:Tag)
        WHERE t.id IN $tagIds
        MATCH (t)-[:TAGGED_WITH]->(n:KnowledgeNode)<-[:TAGGED_WITH]-(l:TagLevel)
        WHERE l.name IN $zoomLevels
          AND (n.status IS NULL OR n.status <> 'pending_approval')
        RETURN DISTINCT n.id AS NodeId, t.id AS TagId, l.name AS TagLevel
    ";
        var result = await session.RunAsync(query, new { tagIds = tagIds.Select(id => id.ToString()), zoomLevels });
        var records = await result.ToListAsync();

        return records
            .Select(r => (
                NodeId: Guid.Parse(r["NodeId"].As<string>()),
                TagId: Guid.Parse(r["TagId"].As<string>()),
                TagLevel: r["TagLevel"].As<string>()
            ))
            .ToHashSet();
    }

    public async Task<HashSet<Guid>> GetTagsRelatedToNodeAsync(Guid nodeId)
    {
        using var session = _driver.AsyncSession();
        var query = @"
        MATCH (t:Tag)-[:TAGGED_WITH]->(n:KnowledgeNode)
        WHERE n.id = $nodeId
        RETURN t.id AS TagId
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
        WHERE n.id IN $nodeIds
        RETURN t.id AS TagId
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
        WHERE n.id = $nodeId
        RETURN t.id AS TagId, r.id AS ResourceId
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
        RETURN t.id AS TagId
    ";

        using var session = _driver.AsyncSession();

        var result = await session.RunAsync(query);

        return await result.ToListAsync(record => Guid.Parse(record["TagId"].As<string>()));
    }

    public async Task<HashSet<Guid>> GetAllDescendantTagIdsAsync(IEnumerable<string> tagIds)
    {
        using var session = _driver.AsyncSession();

        if (!tagIds.Any()) return new HashSet<Guid>();

        var query = @"
        MATCH (parent:Tag)-[:CONTAIN*]->(child:Tag)
        WHERE parent.id IN $tagIds
        RETURN DISTINCT child.id AS tagId";

        var parameters = new { tagIds };

        var result = await session.RunAsync(query, parameters);
        var records = await result.ToListAsync();
        return records.Select(record => Guid.Parse(record["tagId"].As<string>())).ToHashSet();
    }

    public async Task<List<TagNodeGroup>> GetVennTagNodeGroupsAsync()
    {
        using var session = _driver.AsyncSession();
        var cypher = @"
        MATCH (t:Tag)-[:TAGGED_WITH]->(n:KnowledgeNode)
        WHERE (n.status IS NULL OR n.status <> 'pending_approval')
        RETURN t.id AS TagId, n.id AS NodeId
    ";

        var result = await session.RunAsync(cypher);
        var records = await result.ToListAsync();

        return records
        .Select(r => new TagNodeGroup
        {
            TagId = Guid.Parse(r["TagId"].As<string>()), // Convert TagId once
            NodeIds = r["NodeIds"].As<List<string>>()
                .Select(nodeId => Guid.Parse(nodeId))  // Convert NodeIds once
                .Distinct()
                .ToList()
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
        WHERE (n.status IS NULL OR n.status <> 'pending_approval')
          AND t2.name IN $zoomLevels
        RETURN t.id AS TagId, n.id AS NodeId
    ";

            var result = await session.RunAsync(cypher, new { zoomLevels });
            // 获取查询结果
            var records = await result.ToListAsync();

            return records
                .GroupBy(r => r["TagId"].As<string>())
                .Select(g => new TagNodeGroup
                {
                    TagId = Guid.Parse(g.Key),
                    NodeIds = g.Select(r => Guid.Parse(r["NodeId"].As<string>())).Distinct().ToList()
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
        MATCH (n:KnowledgeNode {id: nodeId})-[:HAS_RESOURCE]->(r:Resource)
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
        MATCH (n:KnowledgeNode {id: $nodeId})-[:HAS_RESOURCE]->(r:Resource)
        RETURN r.id AS ResourceId";

        var result = await session.RunAsync(query, new { nodeId });

        return (await result.ToListAsync())
            .Select(record => record["ResourceId"].As<string>())
            .ToList();
    }

    public async Task CreateNodeAndResourceInGraphAsync(string nodeId, string name, string description, IEnumerable<string> links, string userId)
    {
        var query = @"
        MERGE (n:KnowledgeNode {id: $nodeId})
        SET n.name = $name, n.description = $description
        SET n.status = 'pending_approval'
        WITH n
        UNWIND $links AS l
        MERGE (r:Resource {link: l})
        SET r.status = 'pending_approval'
        MERGE (n)-[:HAS_RESOURCE {status: 'pending_approval', contributor: $userId}]->(r)
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
            WHERE rel.userId = $userId AND rel.status <> 'pending_approval'
            RETURN COUNT(rel) AS linkCount
        ", new { userId });

        if (await result.FetchAsync())
        {
            return result.Current["linkCount"].As<int>();
        }

        return 0;
    }

    public async Task<bool> LinkResourceToNodeAsync(string nodeName, string resourceId)
    {
        var query = @"
            MATCH (n:KnowledgeNode)
            WHERE n.name = $nodeName
            MERGE (r:Resource {id: $resourceId})
            MERGE (n)-[:HAS_RESOURCE]->(r)
            SET r.status = 'pending_approval'
            RETURN r";

        try
        {
            using var session = _driver.AsyncSession();
            var result = await session.RunAsync(query, new { nodeName, resourceId });
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
        var query = @"MERGE (t:Tag {id: $tagId})";
        using var session = _driver.AsyncSession();
        var results = await session.RunAsync(query, new Dictionary<string, object> { { "tagId", tagId.ToString() } });
        await results.FetchAsync();
    }

    public async Task RelateTagToNodeAsync(string tagId, string nodeId)
    {
        var query = @"
        MATCH (t:Tag {id: $tagId})
        MATCH (n:KnowledgeNode {id: $nodeId})
        MERGE (t)-[:TAGGED_WITH {status: 'pending_approval'}]->(n)";

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
        MERGE (t:Tag {id: $tagId})
        SET t.status = 'pending_approval'";

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
        MATCH (t:Tag {id: $tagId})
        REMOVE t.status";

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
        MATCH (t:Tag {id: $tagId})
        SET t.status = 'rejected'";

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

    public async Task DetachResourcesFromNodeAsync(string nodeId)
    {
        var query = @"
        MATCH (n:KnowledgeNode {id: $nodeId})-[r:LINKED_TO]->(:Resource)
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
        MATCH (n:KnowledgeNode {id: $nodeId})-[r:LINKED_TO]->(res:Resource)
        REMOVE r.status";

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

    public async Task<HashSet<Guid>> GetAdjacentNodesByLevelAsync(IEnumerable<Guid> parentNodeIds, string targetLevel)
    {
        using var session = _driver.AsyncSession();

        var query = @"
        MATCH (p:KnowledgeNode)<-[:TAGGED_WITH]-(pt:Tag)
        MATCH (pt)-[:CONTAIN*0..]->(ct:Tag)-[:TAGGED_WITH]->(c:KnowledgeNode)<-[:TAGGED_WITH]-(lvl:TagLevel {name: $targetLevel})
        WHERE p.id IN $parentIds AND (c.status IS NULL OR c.status <> 'pending_approval')
        RETURN DISTINCT c.id AS NodeId";

        var parameters = new Dictionary<string, object>
        {
            { "parentIds", parentNodeIds.Select(id => id.ToString()) },
            { "targetLevel", targetLevel }
        };

        var result = await session.RunAsync(query, parameters);
        var records = await result.ToListAsync();

        return records.Select(r => Guid.Parse(r["NodeId"].As<string>())).ToHashSet();
    }
}
