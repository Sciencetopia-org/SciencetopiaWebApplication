using Neo4j.Driver;

public class KnowledgeGraphService
{
    private readonly IDriver _driver;

    public KnowledgeGraphService(IDriver driver)
    {
        _driver = driver;
    }

    public async Task<List<object>> FetchKnowledgeGraphData()
    {
        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
        // Fetch knowledge nodes and their relationships
        MATCH (n)-[r]->(m)
        WHERE (n:Subject OR n:Field OR n:Topic OR n:Keyword OR n:People OR n:Works OR n:Event) AND
              (m:Subject OR m:Field OR m:Topic OR m:Keyword OR m:People OR m:Works OR m:Event)
        // Optionally match resources linked to these nodes
        OPTIONAL MATCH (n)-[:HAS_RESOURCE]->(nr:Resource)
        OPTIONAL MATCH (m)-[:HAS_RESOURCE]->(mr:Resource)
        WITH n, m, r, COLLECT(DISTINCT nr.link) AS nResources, COLLECT(DISTINCT mr.link) AS mResources
        RETURN n AS sourceNode, m AS targetNode, r AS relationship, nResources, mResources
        LIMIT 1000
    ");

        var data = new List<object>();

        await foreach (var record in result)
        {
            var sourceNode = record["sourceNode"].As<INode>();
            var targetNode = record["targetNode"].As<INode>();
            var relationship = record["relationship"].As<IRelationship>();
            var nResources = record["nResources"].As<List<string>>();
            var mResources = record["mResources"].As<List<string>>();

            data.Add(new
            {
                source = new
                {
                    Identity = sourceNode.Id,
                    Labels = sourceNode.Labels.ToList(),
                    Properties = new
                    {
                        Name = sourceNode.Properties["name"].As<string>(),
                        Description = sourceNode.Properties["description"].As<string>(),
                    },
                    Resources = nResources.Select(link => new { Link = link }).ToList()
                },
                target = new
                {
                    Identity = targetNode.Id,
                    Labels = targetNode.Labels.ToList(),
                    Properties = new
                    {
                        Name = targetNode.Properties["name"].As<string>(),
                        Description = targetNode.Properties["description"].As<string>(),
                    },
                    Resources = mResources.Select(link => new { Link = link }).ToList()
                },
                relationship = new
                {
                    Identity = relationship.Id,
                    Start = relationship.StartNodeId,
                    End = relationship.EndNodeId,
                    Type = relationship.Type
                }
            });
        }

        return data;
    }

    // public async Task<List<object>> FetchKnowledgeGraphData(string? userId = null)
    // {
    //     using var session = _driver.AsyncSession();
    //     string query = @"
    //     // Fetch knowledge nodes and their optional relationships
    //     MATCH (n)
    //     WHERE (n:Subject OR n:Field OR n:Topic OR n:Keyword OR n:People OR n:Works OR n:Event)
    //     AND NOT (n:pending_approval) AND NOT (n:disapproved)
    //     OPTIONAL MATCH (n)-[r]-(m)
    //     WHERE (m:Subject OR m:Field OR m:Topic OR m:Keyword OR m:People OR m:Works OR m:Event)
    //     AND NOT (m:pending_approval) AND NOT (n:disapproved)
    //     OPTIONAL MATCH (n)-[:HAS_RESOURCE]->(nr:Resource)
    //     OPTIONAL MATCH (m)-[:HAS_RESOURCE]->(mr:Resource)";

    //     // Extend the query for logged-in users to also fetch their pending nodes
    //     if (!string.IsNullOrEmpty(userId))
    //     {
    //         query += @"
    //         OPTIONAL MATCH (u:User {id: $userId})-[:CREATED]->(np:pending_approval)
    //         WHERE u.id = $userId
    //         OPTIONAL MATCH (np)-[rp]-(mp)
    //         WHERE (mp:pending_approval)
    //         OPTIONAL MATCH (np)-[:HAS_RESOURCE]->(nrp:Resource)
    //         OPTIONAL MATCH (mp)-[:HAS_RESOURCE]->(mrp:Resource)";
    //     }

    //     query += @"
    //     // Aggregate all nodes, relationships, and resources
    //     RETURN COLLECT(DISTINCT n) + COLLECT(DISTINCT np) AS sourceNodes, 
    //     COLLECT(DISTINCT m) + COLLECT(DISTINCT mp) AS targetNodes, 
    //     COLLECT(DISTINCT r) + COLLECT(DISTINCT rp) AS relationships,
    //     COLLECT(DISTINCT nr.link) + COLLECT(DISTINCT nrp.link) AS nResources,
    //     COLLECT(DISTINCT mr.link) + COLLECT(DISTINCT mrp.link) AS mResources
    //     LIMIT 1000";

    //     var result = await session.RunAsync(query, new { userId });

    //     var data = new List<object>();

    //     await foreach (var record in result)
    //     {
    //         var sourceNodes = record["sourceNodes"].As<List<INode>>();
    //         var targetNodes = record["targetNodes"].As<List<INode>>();
    //         var relationships = record["relationships"].As<List<IRelationship>>();
    //         var nResources = record["nResources"].As<List<string>>();
    //         var mResources = record["mResources"].As<List<string>>();

    //         foreach (var sourceNode in sourceNodes)
    //         {
    //             var targetNodeData = targetNodes.Select(targetNode => new
    //             {
    //                 Identity = targetNode?.Id,
    //                 Labels = targetNode?.Labels.ToList(),
    //                 Properties = new
    //                 {
    //                     Name = targetNode.Properties.ContainsKey("name") ? targetNode.Properties["name"].As<string>() : null,
    //                     Description = targetNode.Properties.ContainsKey("description") ? targetNode.Properties["description"].As<string>() : null
    //                 },
    //                 Resources = mResources.Select(link => new { Link = link }).ToList()
    //             }).ToList();

    //             data.Add(new
    //             {
    //                 source = new
    //                 {
    //                     Identity = sourceNode.Id,
    //                     Labels = sourceNode.Labels.ToList(),
    //                     Properties = new
    //                     {
    //                         Name = sourceNode.Properties.ContainsKey("name") ? sourceNode.Properties["name"].As<string>() : "Unnamed",
    //                         Description = sourceNode.Properties.ContainsKey("description") ? sourceNode.Properties["description"].As<string>() : "No description available",
    //                     },
    //                     Resources = nResources.Select(link => new { Link = link }).ToList()
    //                 },
    //                 targets = targetNodeData,
    //                 relationships = relationships.Select(r => new
    //                 {
    //                     Identity = r.Id,
    //                     Start = r.StartNodeId,
    //                     End = r.EndNodeId,
    //                     Type = r.Type
    //                 }).ToList()
    //             });
    //         }
    //     }

    //     return data;
    // }

    public async Task<object> SearchNodeAsync(string query)
    {
        string lowerCaseQuery = query.ToLower();

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
        MATCH (n)
        WHERE (n:Subject OR n:Topic OR n:Keyword OR n:Tag) AND toLower(n.name) CONTAINS $lowerCaseQuery
        RETURN n, 
               CASE WHEN toLower(n.name) STARTS WITH $lowerCaseQuery THEN 1 ELSE 0 END AS startsWithScore,
               CASE WHEN toLower(n.name) = $lowerCaseQuery THEN 1 ELSE 0 END AS exactMatchScore
        ORDER BY exactMatchScore DESC, startsWithScore DESC, n.name
        LIMIT 1
    ", new { lowerCaseQuery });

        if (await result.FetchAsync())
        {
            var node = result.Current["n"].As<INode>();
            return new
            {
                Identity = node.Id,
                Labels = node.Labels.ToList(),
                Properties = new
                {
                    Link = node.Properties.GetValueOrDefault("link", null)?.As<string>(),
                    Name = node.Properties.GetValueOrDefault("name", null)?.As<string>(),
                    Description = node.Properties.GetValueOrDefault("description", null)?.As<string>()
                }
            };
        }

        return null; // Return null if no node is found
    }

    public async Task<string> CreateNodeAsync(CreateNodeRequest request, string userId)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Node name is required.");

        using var session = _driver.AsyncSession();

        // Check if the node name already exists
        var nameExistsResult = await session.RunAsync(@"
            MATCH (n:{request.Label})
            WHERE toLower(n.name) = toApiModel{name.ToLower()})
            RETURN n",
            new { name = request.Name });

        if (await nameExistsResult.FetchAsync())
        {
            throw new InvalidOperationException("Node name already exists.");
        }

        var result = await session.RunAsync(@"
            CREATE (n:{request.Label} {name: $name, description: $description})
            SET n:pending_approval
            WITH n
            UNWIND $link AS l
            CREATE (r:Resource {link: l})
            CREATE (n)-[:HAS_RESOURCE]->(r)
            SET r:pending_approval
            WITH n
            MATCH (u:User {id: $userId})
            CREATE (u)-[:CREATED]->(n)
            RETURN n",
            new { name = request.Name, description = request.Description, link = request.Link, userId });

        return "Node created successfully.";
    }

    public async Task<bool> CreateRelationshipAsync(object sourceNodeName, object targetNodeName, object relationshipType, string userId)
    {
        if (sourceNodeName == null || targetNodeName == null || relationshipType == null)
            throw new ArgumentNullException("Source node name, target node name, and relationship type are all required.");

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (source), (target)
            WHERE source.name = $sourceNodeName AND target.name = $targetNodeName
            CREATE (source)-[:$relationshipType {status: 'pending_approval', contributor: $userId}]->(target)
            RETURN source, target",
            new { sourceNodeName, targetNodeName, relationshipType, userId });

        return await result.FetchAsync(); // True if the operation was successful
    }

    public async Task<bool> ApproveNodeAsync(string nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
            throw new ArgumentException("Node name is required.");

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (n)-[:HAS_RESOURCE]->(r)
            WHERE n.name = $nodeName AND EXISTS(n.pending_approval)
            REMOVE n:pending_approval, r:pending_approval
            RETURN n",
            new { nodeName });

        return await result.FetchAsync(); // True if the node was found and updated
    }

    public async Task<bool> DisapproveNodeAsync(string nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
            throw new ArgumentException("Node name is required.");

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (n)-[:HAS_RESOURCE]->(r)
            WHERE n.name = $nodeName AND EXISTS(n.pending_approval)
            REMOVE n:pending_approval, r:pending_approval
            SET n:disapproved, r:disapproved
            RETURN n",
            new { nodeName });

        return await result.FetchAsync(); // True if the operation was successful
    }

    public async Task<bool> ResubmitNodeAsync(string nodeName)
    {
        if (string.IsNullOrWhiteSpace(nodeName))
            throw new ArgumentException("Node name is required.");

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (n)-[:HAS_RESOURCE]->(r)
            WHERE n.name = $nodeName AND EXISTS(n.disapproved)
            REMOVE n:disapproved, r:disapproved
            SET n:pending_approval, r:pending​​_approval
            RETURN n",
            new { nodeName });

        return await result.FetchAsync(); // True if the operation was successful
    }

    public async Task<bool> ApproveRelationshipAsync(string sourceNodeName, string targetNodeName, string relationshipType)
    {
        if (string.IsNullOrWhiteSpace(sourceNodeName) || string.IsNullOrWhiteSpace(targetNodeName) || string.IsNullOrWhiteSpace(relationshipType))
            throw new ArgumentException("Source node name, target node name, and relationship type are all required.");

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (source)-[r:$relationshipType]->(target)
            WHERE source.name = $sourceNodeName AND target.name = $targetNodeName AND EXISTS(r.pending_approval)
            REMOVE r.pending_approval
            RETURN source, target",
            new { sourceNodeName, targetNodeName, relationshipType });

        return await result.FetchAsync(); // True if the operation was successful
    }

    public async Task<bool> DisapproveRelationshipAsync(string sourceNodeName, string targetNodeName, string relationshipType)
    {
        if (string.IsNullOrWhiteSpace(sourceNodeName) || string.IsNullOrWhiteSpace(targetNodeName) || string.IsNullOrWhiteSpace(relationshipType))
            throw new ArgumentException("Source node name, target node name, and relationship type are all required.");

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (source)-[r:$relationshipType]->(target)
            WHERE source.name = $sourceNodeName AND target.name = $targetNodeName AND EXISTS(r.pending_approval)
            REMOVE r.pending_approval
            SET r:disapproved
            RETURN source, target",
            new { sourceNodeName, targetNodeName, relationshipType });

        return await result.FetchAsync(); // True if the operation was successful
    }

    public async Task<bool> ResubmitRelationshipAsync(string sourceNodeName, string targetNodeName, string relationshipType)
    {
        if (string.IsNullOrWhiteSpace(sourceNodeName) || string.IsNullOrWhiteSpace(targetNodeName) || string.IsNullOrWhiteSpace(relationshipType))
            throw new ArgumentException("Source node name, target node name, and relationship type are all required.");

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (source)-[r:$relationshipType]->(target)
            WHERE source.name = $sourceNodeName AND target.name = $targetNodeName AND EXISTS(r.disapproved)
            REMOVE r.disapproved
            SET r:pending_approval
            RETURN source, target",
            new { sourceNodeName, targetNodeName, relationshipType });

        return await result.FetchAsync(); // True if the operation was successful
    }

    public async Task<bool> AddResourceAsync(string nodeName, string link)
    {
        if (string.IsNullOrWhiteSpace(nodeName) || string.IsNullOrWhiteSpace(link))
            throw new ArgumentException("Node name and link are required.");

        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (n)
            WHERE n.name = $nodeName
            CREATE (r:Resource {link: $link})
            CREATE (n)-[:HAS_RESOURCE]->(r)
            SET r:pending_approval
            RETURN n",
            new { nodeName, link });

        return await result.FetchAsync(); // True if the operation was successful
    }

    public async Task<List<object>> GetPendingNodesAsync()
    {
        var data = new List<object>();
        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (u:User)-[:CREATED]->(n:pending_approval)
            OPTIONAL MATCH (n)-[:HAS_RESOURCE]->(r:Resource)
            RETURN n, r, u.id AS userId",
            new { });

        await foreach (var record in result)
        {
            var node = record["n"].As<INode>();
            var resource = record["r"].As<INode>();
            var userId = record["userId"].As<string>();

            data.Add(new
            {
                Node = new
                {
                    Identity = node.Id,
                    Labels = node.Labels.ToList(),
                    Properties = new
                    {
                        Name = node.Properties["name"].As<string>(),
                        Description = node.Properties["description"].As<string>()
                    },
                    Resource = resource != null ? new
                    {
                        Identity = resource.Id,
                        Labels = resource.Labels.ToList(),
                        Properties = new
                        {
                            Link = resource.Properties["link"].As<string>()
                        }
                    } : null
                },
                UserId = userId
            });
        }

        return data;
    }

    public async Task<List<object>> GetPendingNodesByUserIdAsync(string userId)
    {
        var data = new List<object>();
        using var session = _driver.AsyncSession();
        var result = await session.RunAsync(@"
            MATCH (u:User {id: $userId})-[:CREATED]->(n:pending_approval)
            OPTIONAL MATCH (n)-[:HAS_RESOURCE]->(r:Resource)
            RETURN n, r",
            new { userId });

        await foreach (var record in result)
        {
            var node = record["n"].As<INode>();
            var resource = record["r"].As<INode>();

            data.Add(new
            {
                Node = new
                {
                    Identity = node.Id,
                    Labels = node.Labels.ToList(),
                    Properties = new
                    {
                        Name = node.Properties["name"].As<string>(),
                        Description = node.Properties["description"].As<string>()
                    },
                    Resource = resource != null ? new
                    {
                        Identity = resource.Id,
                        Labels = resource.Labels.ToList(),
                        Properties = new
                        {
                            Link = resource.Properties["link"].As<string>()
                        }
                    } : null
                },
            });
        }
        return data;
    }

    public async Task<List<int>> CountContributedNodesAndLinks(string userId)
    {
        var data = new List<int>();

        try
        {
            using var session = _driver.AsyncSession();

            // Query to count nodes
            var resultNodesCursor = await session.RunAsync(@"
            MATCH (u:User {id: $userId})-[:CREATED]->(n)
            WHERE (n:Subject OR n:Field OR n:Topic OR n:Keyword OR n:People OR n:Works OR n:Event)
            AND NOT (n:pending_approval)
            RETURN COUNT(n) AS nodeCount", new { userId });

            if (!await resultNodesCursor.FetchAsync())
            {
                throw new Exception("Failed to fetch node count.");
            }

            var nodeCountRecord = resultNodesCursor.Current;
            int nodeCount = nodeCountRecord["nodeCount"].As<int>();

            // Query to count links
            var resultLinksCursor = await session.RunAsync(@"
            MATCH (n)-[rel {userId: $userId}]->(m)
            WHERE (n:Subject OR n:Field OR n:Topic OR n:Keyword OR n:People OR n:Works OR n:Event) AND
                  (m:Subject OR m:Field OR m:Topic OR m:Keyword OR m:People OR m:Works OR m:Event)
            RETURN COUNT(rel) AS linkCount", new { userId });

            if (!await resultLinksCursor.FetchAsync())
            {
                throw new Exception("Failed to fetch link count.");
            }

            var linkCountRecord = resultLinksCursor.Current;
            int linkCount = linkCountRecord["linkCount"].As<int>();

            data.Add(nodeCount);
            data.Add(linkCount);
        }
        catch (Exception ex)
        {
            // Log detailed error information
            Console.WriteLine($"Error in CountContributedNodesAndLinks: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            throw new Exception("An error occurred while counting contributed nodes and links", ex);
        }

        return data;
    }

}