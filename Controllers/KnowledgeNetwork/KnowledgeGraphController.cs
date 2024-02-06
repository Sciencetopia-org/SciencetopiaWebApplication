using Microsoft.AspNetCore.Mvc;
using Neo4j.Driver;
using System.Collections.Generic;
using System.Linq;

namespace Sciencetopia.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class KnowledgeGraphController : ControllerBase
    {
        private readonly IDriver _driver;

        public KnowledgeGraphController(IDriver driver)
        {
            // Initialize Neo4j driver
            _driver = driver;
        }

        [HttpGet("GetNodes")]
        public async Task<IActionResult> GetKnowledgeGraph()
        {
            using var session = _driver.AsyncSession();
            var result = await session.RunAsync(@"
        // Fetch knowledge nodes and their relationships
        MATCH (n)-[r]->(m)
        WHERE (n:Field OR n:Topic OR n:Keyword OR n:People OR n:Works OR n:Event) AND
              (m:Field OR m:Topic OR m:Keyword OR m:People OR m:Works OR m:Event)
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

            return Ok(data);
        }


        [HttpGet("Search")]
        public async Task<IActionResult> SearchNode([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("Search query is required.");
            }

            string lowerCaseQuery = query.ToLower();

            using var session = _driver.AsyncSession();
            var result = await session.RunAsync(@"
        MATCH (n)
        WHERE (n:Topic OR n:Keyword OR n:Tag) AND toLower(n.name) CONTAINS $lowerCaseQuery
        RETURN n, 
               CASE WHEN toLower(n.name) STARTS WITH $lowerCaseQuery THEN 1 ELSE 0 END AS startsWithScore,
               CASE WHEN toLower(n.name) = $lowerCaseQuery THEN 1 ELSE 0 END AS exactMatchScore
        ORDER BY exactMatchScore DESC, startsWithScore DESC, n.name
        LIMIT 1
    ", new { lowerCaseQuery });

            if (await result.FetchAsync())
            {
                var node = result.Current["n"].As<INode>();
                return Ok(new
                {
                    Identity = node.Id,
                    Labels = node.Labels.ToList(),
                    Properties = new
                    {
                        Link = node.Properties.GetValueOrDefault("link", null)?.As<string>(),
                        Name = node.Properties.GetValueOrDefault("name", null)?.As<string>(),
                        Description = node.Properties.GetValueOrDefault("description", null)?.As<string>()
                    }
                });
            }

            return NotFound("No node found matching the query.");
        }
    }
}