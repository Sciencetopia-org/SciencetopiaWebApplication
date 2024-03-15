using Microsoft.AspNetCore.Mvc;
using Neo4j.Driver;

namespace SciencetopiaWebApplication.Controllers.SearchEngine
{
    [ApiController]
    [Route("api/[controller]")]
    public class SearchController : ControllerBase
    {
        private readonly IDriver _driver;

        public SearchController(IDriver driver)
        {
            _driver = driver;
        }

        [HttpGet("SearchKnowledgeBase")]
        public async Task<IActionResult> SearchAsync(string query)
        {
            using (var session = _driver.AsyncSession())
            {
                // Perform your search logic here using the Neo4j driver
                // Example: Execute a Cypher query to search the database
                var result = await session.RunAsync(
                    "MATCH (n) WHERE toLower(n.name) CONTAINS toLower($query) OR toLower(n.description) CONTAINS toLower($query) RETURN n",
                    new { query }
                );

                var searchResults = await result.ToListAsync(record => record["n"].As<INode>().Properties);

                return Ok(searchResults);
            }
        }
    }
}