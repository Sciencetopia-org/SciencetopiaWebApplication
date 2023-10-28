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

        [HttpGet]
        public async Task<IActionResult> GetKnowledgeGraph()
        {
            using var session = _driver.AsyncSession();
            var result = await session.RunAsync(@"
                MATCH (n)
                WITH n, SIZE([(n)-[:LINKS_TO]-() | 1]) AS degree
                ORDER BY degree DESC
                LIMIT 20
                WITH COLLECT(n) AS topNodes
                UNWIND topNodes AS n
                MATCH (n)-[r:LINKS_TO]->(m)
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
    }
}