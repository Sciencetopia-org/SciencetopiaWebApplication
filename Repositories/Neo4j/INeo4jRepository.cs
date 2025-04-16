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
    Task<List<TaggedRelationDTO>> GetTaggedRelationsAsync();

    /// <summary>
    /// 获取知识节点之间的层次关系（知识层次关系），
    /// 即标签知识节点之间按照标签体系建立的父→子关系
    /// </summary>
    Task<List<TagContainRelationDTO>> GetPureTagContainRelationsAsync();

    /// <summary>
    /// 利用纯标签节点和 TAGGED_WITH 关系进行二部图投影，
    /// 获取共享同一标签的知识节点之间的投影边，累加权重
    /// </summary>
    Task<List<ProjectedRelationDTO>> GetProjectedRelationsAsync();

    /// <summary>
    /// 获取知识节点与标签层次节点之间的关系
    /// </summary>
    Task<Dictionary<string, string>> GetNodeTagLevelRelationsAsync(IEnumerable<string> nodeIds);

    /// <summary>
    /// 获取所有与指定标签相关的知识节点
    /// </summary>
    Task<HashSet<string>> GetAllNodesRelatedToTagsAsync(IEnumerable<string> tagIds);

    /// <summary>
    /// 获取所有与指定知识节点相关的标签
    /// </summary>
    Task<HashSet<string>> GetAllTagsRelatedToNodesAsync(IEnumerable<string> nodeIds);
    Task<HashSet<string>> GetTagsRelatedToNodeAsync(string nodeId);
    /// <summary>
    /// 根据标签类型获取标签节点的 Id
    /// </summary>
    Task<IEnumerable<string>> GetNodeIdsByLabelAsync(string label);

    /// <summary>
    /// 获取所有与指定标签相关的知识节点
    /// </summary>
    Task<HashSet<string>> GetAllDescendantTagIdsAsync(IEnumerable<string> tagNames);

    /// <summary>
    /// 获取 Venn 图中标签与知识节点的分组
    /// </summary>
    Task<List<TagNodeGroup>> GetVennTagNodeGroupsAsync();
    Task<List<string>> GetLinkedResourceIdsAsync(IEnumerable<string> knowledgeNodeIds);
    Task CreateNodeAndResourceInGraphAsync(Guid nodeId, string name, string description, IEnumerable<string> links, string userId);
    Task<int> CountApprovedLinksByUserAsync(string userId);
    Task<bool> LinkResourceToNodeAsync(string nodeName, string resourceId);
    Task CreateTagNodeIfNotExistsAsync(Guid tagId);
    Task RelateTagToNodeAsync(Guid tagId, Guid nodeId);
    Task CreatePendingTagNodeAsync(Guid tagId);
    Task SetTagStatusApprovedAsync(Guid tagId);
    Task SetTagStatusRejectedAsync(Guid tagId);
    Task DetachResourcesFromNodeAsync(Guid nodeId);
    Task ApproveNodeResourceRelationsAsync(Guid nodeId);
}

public class GraphRepository : IGraphRepository
{
    private readonly IDriver _driver;

    public GraphRepository(IDriver driver)
    {
        _driver = driver;
    }

    public async Task<List<TaggedRelationDTO>> GetTaggedRelationsAsync()
    {
        using var session = _driver.AsyncSession();
        var cypher = @"
            MATCH (t:Tag)-[:TAGGED_WITH]->(n:KnowledgeNode)
            RETURN n.id AS SourceId, t.id AS TagId
        ";
        var result = await session.RunAsync(cypher);
        return (await result.ToListAsync())
            .Select(record => new TaggedRelationDTO
            {
                SourceId = record["SourceId"].As<string>(),
                TagId = record["TagId"].As<string>()
            }).ToList();
    }

    // Repositories/GraphRepository.cs
    public async Task<List<TagContainRelationDTO>> GetPureTagContainRelationsAsync()
    {
        using var session = _driver.AsyncSession();
        var cypher = @"
        MATCH (parent:Tag)-[:CONTAIN]->(child:Tag)
        RETURN parent.id AS ParentTagId, child.id AS ChildTagId
    ";
        var result = await session.RunAsync(cypher);
        return (await result.ToListAsync())
            .Select(record => new TagContainRelationDTO
            {
                ParentTagId = record["ParentTagId"].As<string>(),
                ChildTagId = record["ChildTagId"].As<string>()
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
                SourceId = record["SourceId"].As<string>(),
                TargetId = record["TargetId"].As<string>(),
                Weight = record["weight"].As<int>()
            }).ToList();
    }

    public async Task<Dictionary<string, string>> GetNodeTagLevelRelationsAsync(IEnumerable<string> nodeIds)
    {
        using var session = _driver.AsyncSession();

        var query = @"
        MATCH (tagLevel:TagLevel)-[:TAGGED_WITH]->(node:KnowledgeNode)
        WHERE toLower(node.id) IN $nodeIds
        RETURN node.id AS NodeId, tagLevel.id AS TagLevelId
        ";
        var parameters = new Dictionary<string, object>
        {
            { "nodeIds", nodeIds.Select(id => id.ToLower()) }
        };

        var result = await session.RunAsync(query, parameters);

        var nodeTagLevels = new Dictionary<string, string>();

        await result.ForEachAsync(record =>
        {
            var nodeId = record["NodeId"].As<string>();
            var tagLevelId = record["TagLevelId"].As<string>();
            nodeTagLevels[nodeId] = tagLevelId;
        });

        return nodeTagLevels;
    }

    public async Task<HashSet<string>> GetAllNodesRelatedToTagsAsync(IEnumerable<string> tagIds)
    {
        using var session = _driver.AsyncSession();
        var query = @"
        MATCH (t:Tag)-[:TAGGED_WITH]->(n:KnowledgeNode)
        WHERE t.id IN $tagIds AND NOT n.status <> 'pending_approval'
        RETURN n.id AS NodeId
        ";
        var parameters = new Dictionary<string, object>
        {
            { "tagIds", tagIds.Select(id => id.ToLower()) }
        };

        var result = await session.RunAsync(query, parameters);

        var records = await result.ToListAsync();
        return records.Select(record => record["NodeId"].As<string>()).ToHashSet();
    }

    public async Task<HashSet<string>> GetTagsRelatedToNodeAsync(string nodeId)
    {
        using var session = _driver.AsyncSession();
        var query = @"
        MATCH (t:Tag)-[:TAGGED_WITH]->(n:KnowledgeNode)
        WHERE n.id = $nodeId
        RETURN t.id AS TagId
        ";
        var parameters = new Dictionary<string, object>
        {
            { "nodeId", nodeId.ToLower() }
        };

        var result = await session.RunAsync(query, parameters);

        var records = await result.ToListAsync();
        return records.Select(record => record["TagId"].As<string>()).ToHashSet();
    }

    public async Task<HashSet<string>> GetAllTagsRelatedToNodesAsync(IEnumerable<string> nodeIds)
    {
        using var session = _driver.AsyncSession();
        var query = @"
        MATCH (t:Tag)-[:TAGGED_WITH]->(n:KnowledgeNode)
        WHERE n.id IN $nodeIds
        RETURN t.id AS TagId
        ";
        var parameters = new Dictionary<string, object>
        {
            { "nodeIds", nodeIds.Select(id => id.ToLower()) }
        };

        var result = await session.RunAsync(query, parameters);

        var records = await result.ToListAsync();
        return records.Select(record => record["TagId"].As<string>()).ToHashSet();
    }

    public async Task<IEnumerable<string>> GetNodeIdsByLabelAsync(string label)
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

        return await result.ToListAsync(record => record["TagId"].As<string>());
    }

    public async Task<HashSet<string>> GetAllDescendantTagIdsAsync(IEnumerable<string> tagIds)
    {
        using var session = _driver.AsyncSession();

        if (!tagIds.Any()) return new HashSet<string>();

        var query = @"
        MATCH (parent:Tag)-[:CONTAIN*]->(child:Tag)
        WHERE parent.id IN $tagIds
        RETURN DISTINCT child.id AS tagId";

        var parameters = new { tagIds };

        var result = await session.RunAsync(query, parameters);
        var records = await result.ToListAsync();
        return records.Select(record => record["tagId"].As<string>()).ToHashSet();
    }

    public async Task<List<TagNodeGroup>> GetVennTagNodeGroupsAsync()
    {
        using var session = _driver.AsyncSession();
        var cypher = @"
        MATCH (t:Tag)-[:TAGGED_WITH]->(n:KnowledgeNode)
        RETURN t.id AS TagId, n.id AS NodeId
    ";

        var result = await session.RunAsync(cypher);
        var records = await result.ToListAsync();

        return records
            .GroupBy(r => r["TagId"].As<string>())
            .Select(g => new TagNodeGroup
            {
                TagId = g.Key,
                NodeIds = g.Select(r => r["NodeId"].As<string>()).Distinct().ToList()
            })
            .ToList();
    }

    public async Task<List<string>> GetLinkedResourceIdsAsync(IEnumerable<string> knowledgeNodeIds)
    {
        if (!knowledgeNodeIds.Any())
            return new List<string>();

        var resourceIds = new List<string>();

        var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
        UNWIND $ids AS nodeId
        MATCH (n:KnowledgeNode {id: nodeId})-[:HAS_RESOURCE]->(r:Resource)
        RETURN DISTINCT r.id AS resourceId", new { ids = knowledgeNodeIds });

        await foreach (var record in result)
        {
            var id = record["resourceId"]?.As<string>();
            if (!string.IsNullOrWhiteSpace(id))
                resourceIds.Add(id.ToLower());
        }

        return resourceIds;
    }

    public async Task CreateNodeAndResourceInGraphAsync(Guid nodeId, string name, string description, IEnumerable<string> links, string userId)
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
            nodeId = nodeId.ToString().ToLower(),
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

    public async Task CreateTagNodeIfNotExistsAsync(Guid tagId)
    {
        var query = @"MERGE (t:Tag {id: $tagId})";
        using var session = _driver.AsyncSession();
        var results = await session.RunAsync(query, new Dictionary<string, object> { { "tagId", tagId.ToString() } });
        await results.FetchAsync();
    }

    public async Task RelateTagToNodeAsync(Guid tagId, Guid nodeId)
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

    public async Task CreatePendingTagNodeAsync(Guid tagId)
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

    public async Task SetTagStatusApprovedAsync(Guid tagId)
    {
        var query = @"
        MATCH (t:Tag {id: $tagId})
        SET t.status = 'approved'";

        var parameters = new Dictionary<string, object>
    {
        { "tagId", tagId.ToString() }
    };

        // 开启一个 Neo4j 会话（写模式）
        var session = _driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Write));

        try
        {
            await session.RunAsync(query, parameters);
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    public async Task SetTagStatusRejectedAsync(Guid tagId)
    {
        var query = @"
        MATCH (t:Tag {id: $tagId})
        SET t.status = 'rejected'";

        var session = _driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Write));
        try
        {
            await session.RunAsync(query, new { tagId = tagId.ToString() });
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    public async Task DetachResourcesFromNodeAsync(Guid nodeId)
    {
        var query = @"
        MATCH (n:KnowledgeNode {id: $nodeId})-[r:LINKED_TO]->(:Resource)
        DELETE r";

        var session = _driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Write));
        try
        {
            await session.RunAsync(query, new { nodeId = nodeId.ToString() });
        }
        finally
        {
            await session.CloseAsync();
        }
    }

    public async Task ApproveNodeResourceRelationsAsync(Guid nodeId)
    {
        var query = @"
        MATCH (n:KnowledgeNode {id: $nodeId})-[r:LINKED_TO]->(res:Resource)
        REMOVE r.status";

        var session = _driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Write));
        try
        {
            await session.RunAsync(query, new { nodeId = nodeId.ToString() });
        }
        finally
        {
            await session.CloseAsync();
        }
    }
}
