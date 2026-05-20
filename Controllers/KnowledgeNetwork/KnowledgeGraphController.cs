using Microsoft.AspNetCore.Mvc;
using Neo4j.Driver;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Sciencetopia.Services;
using Sciencetopia.Data;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Services.KnowledgeGraph;
using Sciencetopia.Services.Progress;

namespace Sciencetopia.Controllers.KnowledgeNetwork
{
    [Route("api/[controller]")]
    [ApiController]
    public class KnowledgeGraphController : ControllerBase
    {
        private readonly IDriver _driver;
        private readonly ApplicationDbContext _context;
        private readonly KnowledgeGraphService _knowledgeGraphService;
        private readonly ITagRepository _tagRepository;
        private readonly IGraphRepository _graphRepository;
        private readonly IResourceRepository _resourceRepository;
        private readonly IResourceProgressService _resourceProgressService;

        public KnowledgeGraphController(
            IDriver driver,
            ApplicationDbContext context,
            KnowledgeGraphService knowledgeGraphService,
            ITagRepository tagRepository,
            IGraphRepository graphRepository,
            IResourceRepository resourceRepository,
            IResourceProgressService resourceProgressService)
        {
            // Initialize Neo4j driver
            _driver = driver;
            _context = context;
            _knowledgeGraphService = knowledgeGraphService;
            _tagRepository = tagRepository;
            _graphRepository = graphRepository;
            _resourceRepository = resourceRepository;
            _resourceProgressService = resourceProgressService;
        }

        /// <summary>
        /// 获取知识网络数据，用于前端可视化展示（支持多种视图类型）
        /// </summary>
        /// <param name="tagSystem">标签体系名称，默认为 "MainTag"</param>
        /// <param name="viewType">
        /// 可视化视图类型，支持以下取值：
        /// - "network"：默认值，返回节点-边形式的知识网络图数据（网状图）
        /// - "venn"：返回集合-子集结构的数据，适用于韦恩图展示（标签作为集合，节点作为元素）
        /// 未来可扩展更多视图类型，如："hierarchy"、"heatmap" 等
        /// </param>
        /// <returns>JSON 格式的图数据，用于前端渲染</returns>
        [HttpGet("GetNodes")]
        public async Task<IActionResult> GetKnowledgeGraph(
            [FromQuery] string tagSystem = "MainTag",
            [FromQuery] string viewType = "network",
            [FromQuery] string lang = "zh",
            [FromQuery] bool includePending = false)
        {
            string userId = User?.Identity?.IsAuthenticated == true
                ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty
                : string.Empty;

            var result = await _knowledgeGraphService.GetKnowledgeGraphAsync(tagSystem, viewType, userId, lang, includePending);
            return Ok(result);
        }

        [HttpGet("GetNodeInView")]
        public async Task<IActionResult> GetNodeInView(
            [FromQuery] string tagSystem = "MainTag",
            [FromQuery] string viewType = "network",
            [FromQuery] string zoomLevel = "Field",
            [FromQuery] string lang = "zh",
            [FromQuery] bool includePending = false)
        {
            try
            {
                string userId = User?.Identity?.IsAuthenticated == true
                    ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty
                    : string.Empty;

                // Define the valid zoom levels (lowest -> highest)
                var validZoomLevels = new[] { "Keyword", "Topic", "Field", "Subject", "Discipline" };
                // Validate the zoom level
                if (!validZoomLevels.Contains(zoomLevel))
                {
                    return BadRequest($"Invalid zoom level. Valid options are: {string.Join(", ", validZoomLevels)}");
                }

                // Find the zoom levels greater than or equal to the requested zoom level
                var zoomLevels = validZoomLevels.SkipWhile(z => z != zoomLevel).ToList();

                var result = await _knowledgeGraphService.GetKnowledgeGraphInViewAsync(tagSystem, viewType, zoomLevels, userId, lang, includePending);
                return Ok(result);
            }
            catch (Neo4j.Driver.ServiceUnavailableException)
            {
                return StatusCode(503, new { message = "Graph database is temporarily unavailable. Please try again later." });
            }
            catch (Neo4j.Driver.ConnectionReadTimeoutException)
            {
                return StatusCode(504, new { message = "Graph database request timed out. Please try again." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpPost("LazyLoad")]
        public async Task<IActionResult> LazyLoad([FromBody] LazyLoadRequest request)
        {
            if (request?.ParentIds == null || request.ParentIds.Count == 0)
                return Ok(new List<GraphDTO>());

            var cleanParentIdStrings = request.ParentIds
                .Select(s => s?.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out _))
                .Select(s => s!)                               // 已校验非空
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cleanParentIdStrings.Count == 0)
                return Ok(new List<GraphDTO>());

            // ✅ 这里用干净的 cleanParentIdStrings，而不是原始的 request.ParentIds
            var data = await _knowledgeGraphService.GetAdjacentNodesByLevelAsync(cleanParentIdStrings, request.ZoomLevel ?? "Field");
            return Ok(data != null ? data : new List<GraphDTO>());
        }

        [HttpGet("GetNodeDetails")]
        public async Task<IActionResult> GetNodeDetails(string nodeId, [FromQuery] string lang = "zh")
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return BadRequest("Node ID is required.");
            }

            try
            {
                // Convert nodeId to Guid if necessary
                if (!Guid.TryParse(nodeId, out Guid parsedNodeId))
                {
                    return BadRequest("Invalid Node ID format.");
                }
                var data = await _knowledgeGraphService.GetNodeDetailsByIdAsync(parsedNodeId, lang);
                if (data == null) return NotFound("Node not found.");

                var resourceIds = await _graphRepository.GetResourcesIdsRelatedToNodeAsync(parsedNodeId.ToString());
                var orderedResourceIds = resourceIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (orderedResourceIds.Count > 0)
                {
                    var resources = await _resourceRepository.GetResourcesByIdsAsync(orderedResourceIds);
                    var resourcesById = resources
                        .Where(r => r.Id != Guid.Empty)
                        .GroupBy(r => r.Id.ToString(), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                    var parsedResourceIds = resources
                        .Where(r => r.Id != Guid.Empty)
                        .Select(r => r.Id)
                        .Distinct()
                        .ToList();

                    HashSet<Guid> completedIds = new();
                    string? userId = User?.Identity?.IsAuthenticated == true
                        ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        : null;

                    if (!string.IsNullOrWhiteSpace(userId) && parsedResourceIds.Count > 0)
                    {
                        var completedStatuses = await _resourceProgressService.GetCompletedStatusAsync(userId, parsedResourceIds);
                        completedIds = completedStatuses
                            .Where(x => x.completed)
                            .Select(x => x.resourceId)
                            .ToHashSet();
                    }

                    data.Resources = orderedResourceIds
                        .Select(resourceId =>
                        {
                            if (resourcesById.TryGetValue(resourceId, out var resource))
                            {
                                return new ResourceDTO
                                {
                                    Id = resource.Id.ToString(),
                                    Name = resource.Name ?? resource.Link ?? resourceId,
                                    Link = resource.Link,
                                    Learned = completedIds.Contains(resource.Id)
                                };
                            }

                            return new ResourceDTO
                            {
                                Id = resourceId,
                                Name = resourceId,
                                Link = null,
                                Learned = false
                            };
                        })
                        .ToList();
                }

                return Ok(data);

            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("FilterByTags")]
        public async Task<IActionResult> FilterByTags([FromQuery] List<string> tags, string? tagSystem, [FromQuery] string lang = "zh")
        {
            if (tags == null || tags.Count == 0)
            {
                return BadRequest("At least one tag is required.");
            }

            try
            {
                var tagIds = await _knowledgeGraphService.GetTagIdsByTagNamesAsync(tags);
                var nodeIds = await _knowledgeGraphService.GetNodeIdsByTagsAsync(tagIds);
                var relatedTagIds = tagSystem != null ? await _knowledgeGraphService.GetTagIdsByTagTypeAmongNodesAsync(nodeIds, tagSystem) : await _knowledgeGraphService.GetTagIdsByTagTypeAmongNodesAsync(nodeIds, "MainTag");
                var data = await _knowledgeGraphService.GetKnowledgeGraphDataByNodeId(nodeIds, relatedTagIds, null, lang);
                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("Search")]
        public async Task<IActionResult> SearchNode([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("Search query is required.");
            }

            try
            {
                var result = await _knowledgeGraphService.SearchNodeAsync(query);
                if (result != null)
                {
                    return Ok(result);
                }

                return NotFound("No node found matching the query.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("SearchTags")]
        public async Task<IActionResult> SearchTags([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("Search query is required.");
            }

            try
            {
                var result = await _knowledgeGraphService.SearchTagsAsync(query);
                if (result != null)
                {
                    return Ok(result);
                }

                return NotFound("No tag found matching the query.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("SearchTagNames")]
        public async Task<IActionResult> SearchTagNames([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("Search query is required.");
            }

            try
            {
                var result = await _tagRepository.SearchTagNamesAsync(query);
                if (result != null && result.Count > 0)
                {
                    return Ok(result);
                }

                return NotFound("No tag names found matching the query.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("GetTagSystems")]
        public async Task<IActionResult> GetTagSystems()
        {
            try
            {
                var tagSystems = await _tagRepository.GetAllTagSystemsAsync();
                if (tagSystems != null && tagSystems.Count > 0)
                {
                    return Ok(tagSystems);
                }

                return NotFound("No tag systems found.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("CreateNode")]
        public async Task<IActionResult> CreateNode([FromBody] CreateNodeRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request body is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Node name is required.");
            }

            try
            {
                string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
                var responseMessage = await _knowledgeGraphService.CreateNodeAsync(request, userId);
                return Ok(responseMessage);
            }
            catch (DraftingFrozenException ex)
            {
                return StatusCode(503, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // [HttpPost("CreateRelationship")]
        // public async Task<IActionResult> CreateRelationship([FromBody] CreateRelationshipRequest request)
        // {
        //     if (request == null)
        //     {
        //         return BadRequest("Request body is required.");
        //     }

        //     if (string.IsNullOrWhiteSpace(request.SourceNodeName) || string.IsNullOrWhiteSpace(request.TargetNodeName) || string.IsNullOrWhiteSpace(request.RelationshipType))
        //     {
        //         return BadRequest("Source node name, target node name, and relationship type are all required.");
        //     }

        //     try
        //     {
        //         string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        //         bool success = await _knowledgeGraphService.CreateRelationshipAsync(request.SourceNodeName, request.TargetNodeName, request.RelationshipType, userId);
        //         if (success)
        //             return Ok();
        //         else
        //             return NotFound("Source node or target node not found.");
        //     }
        //     catch (Exception ex)
        //     {
        //         return StatusCode(500, $"Internal server error: {ex.Message}");
        //     }
        // }

        [HttpPost("CreateTag")]
        public async Task<IActionResult> CreateTag([FromBody] CreateTagRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Tag name is required.");
            }

            try
            {
                string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
                await _knowledgeGraphService.CreateTagDraftAsync(request.Name, request.Description, userId);
                return Ok("Tag draft submitted (new or already exists).");
            }
            catch (DraftingFrozenException ex)
            {
                return StatusCode(503, ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("EditNode")]
        public async Task<IActionResult> EditNode([FromBody] EditNodeRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.NodeId.ToString()) || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Node ID and name are required.");
            }

            try
            {
                string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
                var responseMessage = await _knowledgeGraphService.EditNodeAsync(request, userId);
                return Ok(responseMessage);
            }
            catch (DraftingFrozenException ex)
            {
                return StatusCode(503, ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("ApproveNode")]
        [Authorize(Roles = "administrator")]
        public async Task<IActionResult> ApproveNode(Guid versionId)
        {
            if (versionId == Guid.Empty)
            {
                return BadRequest("Version identifier is required.");
            }

            try
            {
                string adminId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
                bool success = await _knowledgeGraphService.ApproveNodeAsync(versionId, adminId);
                if (success)
                    return Ok("Node approval successful.");
                else
                    return NotFound("Node not found or not marked as pending approval.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("RejectNode")]
        [Authorize(Roles = "administrator")]
        public async Task<IActionResult> DisapproveNode(Guid versionId)
        {
            if (versionId == Guid.Empty)
            {
                return BadRequest("Version identifier is required.");
            }

            try
            {
                string adminId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
                bool success = await _knowledgeGraphService.DisapproveNodeAsync(versionId, adminId);
                if (success)
                    return Ok("Node disapproval successful.");
                else
                    return NotFound("Node not found or not marked as pending approval.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("ApproveTag")]
        [Authorize(Roles = "administrator")]
        public async Task<IActionResult> ApproveTag(Guid versionId)
        {
            if (versionId == Guid.Empty)
            {
                return BadRequest("Version identifier is required.");
            }

            try
            {
                string adminId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
                bool success = await _knowledgeGraphService.ApproveTagAsync(versionId, adminId);
                if (success)
                    return Ok("Tag approval successful.");
                else
                    return NotFound("Tag not found or not marked as pending approval.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("RejectTag")]
        [Authorize(Roles = "administrator")]
        public async Task<IActionResult> DisapproveTag(Guid versionId)
        {
            if (versionId == Guid.Empty)
            {
                return BadRequest("Version identifier is required.");
            }

            try
            {
                string adminId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
                bool success = await _knowledgeGraphService.DisapproveTagAsync(versionId, adminId);
                if (success)
                    return Ok("Tag disapproval successful.");
                else
                    return NotFound("Tag not found or not marked as pending approval.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("ResubmitNode")]
        public async Task<IActionResult> ResubmitNode(Guid versionId)
        {
            if (versionId == Guid.Empty)
            {
                return BadRequest("Version identifier is required.");
            }

            try
            {
                string userId = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
                bool success = await _knowledgeGraphService.ResubmitNodeAsync(versionId, userId);
                if (success)
                    return Ok("Node resubmission successful.");
                else
                    return NotFound("Node not found or not marked as disapproved.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // [HttpPost("ApproveRelationship")]
        // [Authorize(Roles = "administrator")]
        // public async Task<IActionResult> ApproveRelationship(string sourceNodeName, string targetNodeName, string relationshipType)
        // {
        //     if (string.IsNullOrWhiteSpace(sourceNodeName) || string.IsNullOrWhiteSpace(targetNodeName) || string.IsNullOrWhiteSpace(relationshipType))
        //     {
        //         return BadRequest("Source node name, target node name, and relationship type are all required.");
        //     }

        //     try
        //     {
        //         bool success = await _knowledgeGraphService.ApproveRelationshipAsync(sourceNodeName, targetNodeName, relationshipType);
        //         if (success)
        //             return Ok("Relationship approval successful.");
        //         else
        //             return NotFound("Relationship not found or not marked as pending approval.");
        //     }
        //     catch (Exception ex)
        //     {
        //         return StatusCode(500, $"Internal server error: {ex.Message}");
        //     }
        // }

        // [HttpPost("RejectRelationship")]
        // [Authorize(Roles = "administrator")]
        // public async Task<IActionResult> DisapproveRelationship(string sourceNodeName, string targetNodeName, string relationshipType)
        // {
        //     if (string.IsNullOrWhiteSpace(sourceNodeName) || string.IsNullOrWhiteSpace(targetNodeName) || string.IsNullOrWhiteSpace(relationshipType))
        //     {
        //         return BadRequest("Source node name, target node name, and relationship type are all required.");
        //     }

        //     try
        //     {
        //         bool success = await _knowledgeGraphService.DisapproveRelationshipAsync(sourceNodeName, targetNodeName, relationshipType);
        //         if (success)
        //             return Ok("Relationship disapproval successful.");
        //         else
        //             return NotFound("Relationship not found or not marked as pending approval.");
        //     }
        //     catch (Exception ex)
        //     {
        //         return StatusCode(500, $"Internal server error: {ex.Message}");
        //     }
        // }

        // [HttpPost("ResubmitRelationship")]
        // public async Task<IActionResult> ResubmitRelationship(string sourceNodeName, string targetNodeName, string relationshipType)
        // {
        //     if (string.IsNullOrWhiteSpace(sourceNodeName) || string.IsNullOrWhiteSpace(targetNodeName) || string.IsNullOrWhiteSpace(relationshipType))
        //     {
        //         return BadRequest("Source node name, target node name, and relationship type are all required.");
        //     }

        //     try
        //     {
        //         bool success = await _knowledgeGraphService.ResubmitRelationshipAsync(sourceNodeName, targetNodeName, relationshipType);
        //         if (success)
        //             return Ok("Relationship resubmission successful.");
        //         else
        //             return NotFound("Relationship not found or not marked as disapproved.");
        //     }
        //     catch (Exception ex)
        //     {
        //         return StatusCode(500, $"Internal server error: {ex.Message}");
        //     }
        // }

        [HttpPost("AddResource")]
        public async Task<IActionResult> AddResource([FromBody] AddResourceRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request body is required.");
            }

            if (string.IsNullOrWhiteSpace(request.NodeName) || string.IsNullOrWhiteSpace(request.Link))
            {
                return BadRequest("Node name and resource link are both required.");
            }

            try
            {
                bool success = await _knowledgeGraphService.AddResourceAsync(request.NodeName, request.Link);
                if (success)
                    return Ok();
                else
                    return NotFound("Node not found.");
            }
            catch (DraftingFrozenException ex)
            {
                return StatusCode(503, ex.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("GetPendingNodes")]
        [Authorize(Roles = "administrator")]
        public async Task<IActionResult> GetPendingNodes()
        {
            try
            {
                var data = await _knowledgeGraphService.GetPendingNodesAsync();
                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("GetPendingNodeByUserId")]
        public async Task<IActionResult> GetPendingNodeByUserId(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest("User ID is required.");
            }

            try
            {
                var data = await _knowledgeGraphService.GetPendingNodesByUserIdAsync(userId);
                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("GetPendingTags")]
        [Authorize(Roles = "administrator")]
        public async Task<IActionResult> GetPendingTags()
        {
            try
            {
                var data = await _knowledgeGraphService.GetPendingTagsAsync();
                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPost("GetPendingTagsByUserId")]
        public async Task<IActionResult> GetPendingTagsByUserId(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest("User ID is required.");
            }

            try
            {
                var data = await _knowledgeGraphService.GetPendingTagsByUserIdAsync(userId);
                return Ok(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpGet("CountContributedNodesAndLinksByUserId")]
        public async Task<IActionResult> CountContributedNodesAndLinks(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest("User ID is required.");
            }

            try
            {
                var data = await _knowledgeGraphService.CountContributedNodesAndLinks(userId);
                return Ok(data);
            }
            catch (Exception ex)
            {
                // Log the detailed error
                Console.WriteLine($"Controller error in CountContributedNodesAndLinks: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}
