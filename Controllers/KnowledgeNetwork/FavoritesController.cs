// This version assumes: 
// (1) each user has one or more Favorite nodes (with `type`, e.g. "favorite")
// (2) Favorite node connects to KnowledgeNode via [:INCLUDES] relationship
// (3) name is stored only in SQL

using Microsoft.AspNetCore.Mvc;
using Neo4j.Driver;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Security.Claims;

namespace Sciencetopia.Controllers.KnowledgeNetwork;

[Route("api/KnowledgeGraph/[controller]")]
[ApiController]
[Authorize]
public class FavoritesController : ControllerBase
{
    private readonly IAsyncSession _session;

    public FavoritesController(IAsyncSession session)
    {
        _session = session;
    }

    [HttpPost("{nodeId}")]
    public async Task<IActionResult> ToggleFavorites(string nodeId)
    {
        try
        {
            if (!Guid.TryParse(nodeId, out Guid parsedNodeId))
            {
                return BadRequest(new { success = false, message = "Invalid node ID format." });
            }
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var query = @"
                MATCH (n {id: $nodeId})
                MERGE (u:User {id: $userId})
                MERGE (u)-[:OWNS]->(f:Favorite {type: 'favorite'})
                WITH f, n
                OPTIONAL MATCH (f)-[r:INCLUDES]->(n)
                WITH f, n, r
                CALL apoc.do.when(
                    r IS NULL,
                    'MERGE (f)-[:INCLUDES {addedAt: datetime()}]->(n) RETURN true AS favorited',
                    'DELETE r RETURN false AS favorited',
                    {f: f, n: n, r: r}
                ) YIELD value
                RETURN value.favorited AS favorited
            ";

            var result = await _session.RunAsync(query, new { userId, nodeId = parsedNodeId.ToString() });
            var peek = await result.PeekAsync();
            if (peek == null)
            {
                return NotFound(new { success = false, message = "Node not found." });
            }

            var record = await result.SingleAsync();

            bool isFavorited = record?["favorited"].As<bool>() ?? false;

            return Ok(new { success = true, nodeId = parsedNodeId, favorited = isFavorited });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("ToggleLearned/{nodeId}")]
    public async Task<IActionResult> ToggleLearningStatus(string nodeId)
    {
        try
        {
            if (!Guid.TryParse(nodeId, out Guid parsedNodeId))
            {
                return BadRequest(new { success = false, message = "Invalid node ID format." });
            }
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var query = @"
                MATCH (n {id: $nodeId})
                MERGE (u:User {id: $userId})
                MERGE (u)-[:OWNS]->(f:Favorite {type: 'learned'})
                WITH f, n
                OPTIONAL MATCH (f)-[r:INCLUDES]->(n)
                WITH f, n, r
                CALL apoc.do.when(
                    r IS NULL,
                    'MERGE (f)-[:INCLUDES {addedAt: datetime()}]->(n) RETURN true AS learned',
                    'DELETE r RETURN false AS learned',
                    {f: f, n: n, r: r}
                ) YIELD value
                RETURN value.learned AS learned
            ";

            var result = await _session.RunAsync(query, new { userId, nodeId = parsedNodeId.ToString() });
            var peek = await result.PeekAsync();
            if (peek == null)
            {
                return NotFound(new { success = false, message = "Node not found." });
            }

            var record = await result.SingleAsync();

            bool isLearned = record?["learned"]?.As<bool>() ?? false;

            return Ok(new { success = true, nodeId = parsedNodeId, learned = isLearned });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("Status/{nodeId}")]
    public async Task<IActionResult> GetFavoriteStatus(string nodeId)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Ok(new { success = true, favorited = false });
            }

            if (!Guid.TryParse(nodeId, out Guid parsedNodeId))
            {
                return BadRequest(new { success = false, message = "Invalid node ID format." });
            }

            var query = @"
                MATCH (n {id: $nodeId})
                OPTIONAL MATCH (u:User {id: $userId})-[:OWNS]->(f:Favorite {type: 'favorite'})-[:INCLUDES]->(n)
                RETURN CASE WHEN f IS NULL THEN false ELSE true END AS favorited
            ";

            var result = await _session.RunAsync(query, new { userId, nodeId = parsedNodeId.ToString() });
            var record = await result.SingleAsync();

            bool isFavorited = record?["favorited"].As<bool>() ?? false;
            return Ok(new { success = true, favorited = isFavorited });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("LearningStatus/{nodeId}")]
    public async Task<IActionResult> GetLearningStatus(string nodeId)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Ok(new { success = true, learned = false });
            }

            if (!Guid.TryParse(nodeId, out Guid parsedNodeId))
            {
                return BadRequest(new { success = false, message = "Invalid node ID format." });
            }

            var query = @"
                MATCH (n {id: $nodeId})
                OPTIONAL MATCH (u:User {id: $userId})-[:OWNS]->(f:Favorite {type: 'learned'})-[:INCLUDES]->(n)
                RETURN CASE WHEN f IS NULL THEN false ELSE true END AS learned
            ";

            var result = await _session.RunAsync(query, new { userId, nodeId = parsedNodeId.ToString() });
            var record = await result.SingleAsync();

            bool isLearned = record?["learned"].As<bool>() ?? false;
            return Ok(new { success = true, learned = isLearned });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("MyFavorites")]
    public async Task<IActionResult> GetMyFavoriteNodes()
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var query = @"
                MATCH (u:User {id: $userId})-[:OWNS]->(f:Favorite {type: 'favorite'})-[:INCLUDES]->(n)
                RETURN n
            ";

            var result = await _session.RunAsync(query, new { userId });
            var data = await result.ToListAsync();

            var response = data.Select(record => new
            {
                identity = record["n"].As<INode>().Id,
                labels = record["n"].As<INode>().Labels,
                properties = record["n"].As<INode>().Properties
            }).ToList();

            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("MyLearned")]
    public async Task<IActionResult> GetMyLearnedNodes()
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var query = @"
                MATCH (u:User {id: $userId})-[:OWNS]->(f:Favorite {type: 'learned'})-[:INCLUDES]->(n)
                RETURN n
            ";

            var result = await _session.RunAsync(query, new { userId });
            var data = await result.ToListAsync();

            var response = data.Select(record => new
            {
                identity = record["n"].As<INode>().Id,
                labels = record["n"].As<INode>().Labels,
                properties = record["n"].As<INode>().Properties
            }).ToList();

            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpDelete("{nodeId}")]
    public async Task<IActionResult> RemoveFromFavorites(string nodeId)
    {
        try
        {
            if (!Guid.TryParse(nodeId, out Guid parsedNodeId))
            {
                return BadRequest(new { success = false, message = "Invalid node ID format." });
            }
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var query = @"
                MATCH (u:User {id: $userId})-[:OWNS]->(f:Favorite {type: 'favorite'})-[r:INCLUDES]->(n {id: $nodeId})
                DELETE r
                RETURN n
            ";

            var result = await _session.RunAsync(query, new { userId, nodeId = parsedNodeId.ToString() });
            if (await result.FetchAsync())
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

    [HttpDelete("Learned/{nodeId}")]
    public async Task<IActionResult> RemoveFromLearned(string nodeId)
    {
        try
        {
            if (!Guid.TryParse(nodeId, out Guid parsedNodeId))
            {
                return BadRequest(new { success = false, message = "Invalid node ID format." });
            }
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var query = @"
                MATCH (u:User {id: $userId})-[:OWNS]->(f:Favorite {type: 'learned'})-[r:INCLUDES]->(n {id: $nodeId})
                DELETE r
                RETURN n
            ";

            var result = await _session.RunAsync(query, new { userId, nodeId = parsedNodeId.ToString() });
            if (await result.FetchAsync())
            {
                return Ok(new { success = true, message = "Node removed from learned list." });
            }
            else
            {
                return NotFound(new { success = false, message = "Learned relation not found." });
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
