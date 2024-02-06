using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neo4j.Driver;
using Sciencetopia.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sciencetopia.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class RecommendationsController : ControllerBase
    {
        private readonly IDriver _driver;

        public RecommendationsController(IDriver driver)
        {
            _driver = driver;
        }

        // GET: api/Recommendations
        [HttpGet]
        public async Task<ActionResult<List<Recommendation>>> Index()
        {
            if (string.IsNullOrEmpty(User.Identity?.Name))
            {
                return BadRequest(new { message = "用户未登录或用户名为空" });
            }

            var recommendations = await GetRecommendationsForUserAsync(User.Identity.Name);
            if (recommendations == null || recommendations.Count == 0)
            {
                return NotFound(new { message = "没有找到任何推荐" });
            }
            return recommendations;
        }

        private async Task<List<Recommendation>> GetRecommendationsForUserAsync(string userName)
        {
            var recommendations = new List<Recommendation>();

            using (var session = _driver.AsyncSession())
            {
                var result = await session.ExecuteReadAsync(async tx =>
                {
                    // 在这里编写您的推荐查询逻辑
                    // 例如：找出用户最常访问的资源类型，并返回与之相关的推荐资源
                    var cypherQuery = @"
                        MATCH (u:User {UserName: $UserName})-[r:VISITED]->(res:Resource)
                        RETURN res.Title as Title, res.Url as Url
                        ORDER BY r.Count DESC
                        LIMIT 5";

                    return await tx.RunAsync(cypherQuery, new { UserName = userName });
                });

                await foreach (var record in result)
                {
                    recommendations.Add(new Recommendation
                    {
                        Title = record["Title"].As<string>(),
                        Url = record["Url"].As<string>()
                    });
                }
            }

            return recommendations;
        }
    }
}
