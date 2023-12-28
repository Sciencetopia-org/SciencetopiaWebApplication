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
                MATCH (n)
                WHERE n:Topic OR n:Keyword OR n:Tag
                WITH n, SIZE([(n)-[]-() | 1]) AS degree
                ORDER BY degree DESC
                LIMIT 1000
                WITH COLLECT(n) AS topNodes
                UNWIND topNodes AS n
                MATCH (n)-[r]->(m)
                WHERE m IN topNodes
                RETURN n, r, m
            ");
            var data = new List<object>();

            await foreach (var record in result)
            {
                var sourceNode = record["n"].As<INode>();
                var targetNode = record["m"].As<INode>();
                var relationship = record["r"].As<IRelationship>();

                data.Add(new
                {
                    source = new
                    {
                        Identity = sourceNode.Id,
                        Labels = sourceNode.Labels.ToList(),
                        Properties = new
                        {
                            Link = sourceNode.Properties["link"].As<string>(),
                            Name = sourceNode.Properties["name"].As<string>(),
                            description = sourceNode.Properties["description"].As<string>()
                        }
                    },
                    target = new
                    {
                        Identity = targetNode.Id,
                        Labels = targetNode.Labels.ToList(),
                        Properties = new
                        {
                            Link = targetNode.Properties["link"].As<string>(),
                            Name = targetNode.Properties["name"].As<string>(),
                            description = targetNode.Properties["description"].As<string>()
                        }
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