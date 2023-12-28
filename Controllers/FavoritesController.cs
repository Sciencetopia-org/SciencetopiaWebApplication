using Microsoft.AspNetCore.Mvc;
using Neo4j.Driver;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Security.Claims;

[Route("api/[controller]")]
[ApiController]
[Authorize] // Ensure only authenticated users can access
public class FavoritesController : ControllerBase
{
    private readonly IAsyncSession _session;

    public FavoritesController(IAsyncSession session)
    {
        _session = session;
    }

    [HttpPost("{nodeId}")]
    public async Task<IActionResult> AddToFavorites(string nodeId)
    {
        try
        {
            int parsedNodeId = Int32.Parse(nodeId);
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value; // 获取当前登录用户的唯一标识符

            var query = @"
            MATCH (u:User {Id: $userId}), (n)
            WHERE id(n) = $nodeId
            MERGE (u)-[:FAVORITED]->(n)
            RETURN n
        ";

            var result = await _session.RunAsync(query, new { userId, nodeId = parsedNodeId });
            var node = await result.SingleAsync();

            return Ok(new { success = true, node });
        }
        catch (ClientException ex)
        {
            if (ex.Message.Contains("not found"))
            {
                return NotFound(new { success = false, message = "Node or User not found." });
            }
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("MyFavorites")]
    public async Task<IActionResult> GetMyFavorites()
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value; // 获取当前登录用户的唯一标识符

            var query = @"
        MATCH (u:User {Id: $userId})-[:FAVORITED]->(favNode)
        OPTIONAL MATCH (favNode)-[rel]-(relatedNode)
        WHERE (relatedNode)-[:FAVORITED]-(u)
        RETURN favNode, rel, relatedNode
    ";

            var result = await _session.RunAsync(query, new { userId });
            var data = await result.ToListAsync();

            var response = new List<object>();
            foreach (var record in data)
            {
                var favNode = record["favNode"].As<INode>();
                var rel = record["rel"].As<IRelationship>();
                var relatedNode = record["relatedNode"].As<INode>();

                response.Add(new
                {
                    source = new
                    {
                        identity = favNode.Id,
                        labels = favNode.Labels,
                        properties = favNode.Properties
                    },
                    relationship = rel == null ? null : new
                    {
                        identity = rel.Id,
                        start = rel.StartNodeId,
                        end = rel.EndNodeId,
                        type = rel.Type,
                        properties = rel.Properties
                    },
                    target = relatedNode == null ? null : new
                    {
                        identity = relatedNode.Id,
                        labels = relatedNode.Labels,
                        properties = relatedNode.Properties
                    }
                });
            }

            return Ok(response);
        }
        catch (ClientException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpDelete("{nodeId}")]
    public async Task<IActionResult> RemoveFromFavorites(string nodeId)
    {
        try
        {
            int parsedNodeId = Int32.Parse(nodeId);
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var query = @"
        MATCH (u:User {Id: $userId})-[r:FAVORITED]->(n)
        WHERE id(n) = $nodeId
        DELETE r
        RETURN n
        ";

            var result = await _session.RunAsync(query, new { userId, nodeId = parsedNodeId });
            if (await result.FetchAsync()) // 检查是否找到并删除了关系
            {
                return Ok(new { success = true, message = "Node removed from favorites." });
            }
            else
            {
                return NotFound(new { success = false, message = "Favorite not found." });
            }
        }
        catch (FormatException)
        {
            return BadRequest(new { success = false, message = "Invalid node ID format." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

}