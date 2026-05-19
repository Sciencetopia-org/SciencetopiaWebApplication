using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Sciencetopia.Data;
using Sciencetopia.Services;
using Sciencetopia.Services.SearchEngine;

namespace Sciencetopia.Controllers.SearchEngine
{
    [ApiController]
    [Route("api/[controller]")]
    public class SearchController : ControllerBase
    {
        private readonly IKnowledgeNodeRepository _knowledgeRepo;
        private readonly SearchService _searchService;
        private readonly StudyGroupService _studyGroupService;
        private readonly SearchVectorService _vectorService;
        private readonly ApplicationDbContext _db;

        public SearchController(
            IKnowledgeNodeRepository knowledgeRepo,
            SearchService searchService,
            StudyGroupService studyGroupService,
            SearchVectorService vectorService,
            ApplicationDbContext db)
        {
            _knowledgeRepo = knowledgeRepo;
            _searchService = searchService;
            _studyGroupService = studyGroupService;
            _vectorService = vectorService;
            _db = db;
        }

        [HttpGet]
        public async Task<IActionResult> SearchAsync(string query, int page = 1, int pageSize = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query parameter is required.");

            var skip = (page - 1) * pageSize;

            // Run sequentially to avoid concurrent DbContext usage across scoped repositories.
            var knowledgeBase = await _knowledgeRepo.SearchKnowledgeNodesAsync(query, skip, pageSize);
            var resources = await _searchService.SearchResourcesWithLinkedNodesAsync(query, skip, pageSize);
            var studyGroups = await _studyGroupService.SearchStudyGroups(query, skip, pageSize);
            var vectorHits = await _vectorService.SearchAsync(query, pageSize * 6);

            knowledgeBase = MergeByKey(
                knowledgeBase,
                await FetchVectorKnowledgeNodesAsync(vectorHits, pageSize),
                x => x.StableId.ToString(),
                pageSize);
            resources = MergeByKey(
                resources,
                await FetchVectorResourcesAsync(vectorHits, pageSize),
                x => x.Id.ToString(),
                pageSize);
            studyGroups = MergeByKey(
                studyGroups,
                await FetchVectorStudyGroupsAsync(vectorHits, pageSize),
                x => x.Id ?? string.Empty,
                pageSize);

            var result = new
            {
                KnowledgeBase = knowledgeBase,
                Resources = resources,
                StudyGroups = studyGroups,
                // StudyPlans = studyPlans
            };
            return Ok(result);
        }

        [HttpGet("SearchKnowledgeBase")]
        public async Task<IActionResult> SearchKnowledgeBaseAsync(string query, int page = 1, int pageSize = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query parameter is required.");

            var skip = (page - 1) * pageSize;
            var result = await _knowledgeRepo.SearchKnowledgeNodesAsync(query, skip, pageSize);
            var vectorHits = await _vectorService.SearchAsync(query, pageSize * 3);
            result = MergeByKey(
                result,
                await FetchVectorKnowledgeNodesAsync(vectorHits, pageSize),
                x => x.StableId.ToString(),
                pageSize);
            return Ok(result);
        }

        [HttpGet("SearchResources")]
        public async Task<IActionResult> SearchResourcesAsync(string query, int page = 1, int pageSize = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query parameter is required.");

            var skip = (page - 1) * pageSize;
            var result = await _searchService.SearchResourcesWithLinkedNodesAsync(query, skip, pageSize);
            var vectorHits = await _vectorService.SearchAsync(query, pageSize * 3);
            result = MergeByKey(
                result,
                await FetchVectorResourcesAsync(vectorHits, pageSize),
                x => x.Id.ToString(),
                pageSize);
            return Ok(result);
        }

        [HttpGet("SearchStudyGroups")]
        public async Task<IActionResult> SearchStudyGroupsAsync(string query, int page = 1, int pageSize = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query parameter is required.");

            var skip = (page - 1) * pageSize;
            var result = await _studyGroupService.SearchStudyGroups(query, skip, pageSize);
            var vectorHits = await _vectorService.SearchAsync(query, pageSize * 3);
            result = MergeByKey(
                result,
                await FetchVectorStudyGroupsAsync(vectorHits, pageSize),
                x => x.Id ?? string.Empty,
                pageSize);
            return Ok(result);
        }

        [HttpPost("ReindexVectors")]
        public async Task<IActionResult> ReindexVectorsAsync(
            [FromQuery] string? query = null,
            [FromQuery] int limit = 64,
            CancellationToken ct = default)
        {
            limit = Math.Clamp(limit, 1, 500);
            try
            {
                var indexed = await _vectorService.RefreshIndexAsync(query, limit, ct);
                return Ok(new { indexed });
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(503, new
                {
                    message = "Embedding provider is unavailable or rate-limited.",
                    detail = ex.Message
                });
            }
        }

        [HttpGet("VectorStatus")]
        public async Task<IActionResult> VectorStatusAsync(CancellationToken ct = default)
        {
            var status = await _vectorService.GetStatusAsync(ct);
            return Ok(status);
        }

        // [HttpGet("SearchStudyPlans")]
        // public async Task<IActionResult> SearchStudyPlansAsync(string query, int page = 1, int pageSize = 10)
        // {
        //     if (string.IsNullOrWhiteSpace(query))
        //         return BadRequest("Query parameter is required.");

        //     var skip = (page - 1) * pageSize;
        //     var result = await _searchService.SearchStudyPlans(query, skip, pageSize);
        //     return Ok(result);
        // }

        private async Task<List<KnowledgeNode>> FetchVectorKnowledgeNodesAsync(
            IReadOnlyList<SearchVectorHit> hits,
            int take)
        {
            var orderedIds = hits
                .Where(x => x.EntityType == SearchVectorService.KnowledgeNodeType && Guid.TryParse(x.EntityId, out _))
                .Select(x => Guid.Parse(x.EntityId))
                .Take(take)
                .ToList();
            if (orderedIds.Count == 0)
            {
                return new List<KnowledgeNode>();
            }

            var rows = await _db.KnowledgeNodes.AsNoTracking()
                .Where(n => orderedIds.Contains(n.StableId) && n.IsCurrent && n.Status == "Current")
                .ToListAsync();

            return orderedIds
                .Select(id => rows.FirstOrDefault(row => row.StableId == id))
                .Where(row => row != null)
                .Cast<KnowledgeNode>()
                .ToList();
        }

        private async Task<List<Resource>> FetchVectorResourcesAsync(
            IReadOnlyList<SearchVectorHit> hits,
            int take)
        {
            var orderedIds = hits
                .Where(x => x.EntityType == SearchVectorService.ResourceType && Guid.TryParse(x.EntityId, out _))
                .Select(x => Guid.Parse(x.EntityId))
                .Take(take)
                .ToList();
            if (orderedIds.Count == 0)
            {
                return new List<Resource>();
            }

            var rows = await _db.Resources.AsNoTracking()
                .Where(r => orderedIds.Contains(r.Id))
                .ToListAsync();

            return orderedIds
                .Select(id => rows.FirstOrDefault(row => row.Id == id))
                .Where(row => row != null)
                .Cast<Resource>()
                .ToList();
        }

        private async Task<List<StudyGroup>> FetchVectorStudyGroupsAsync(
            IReadOnlyList<SearchVectorHit> hits,
            int take)
        {
            var orderedIds = hits
                .Where(x => x.EntityType == SearchVectorService.StudyGroupType && Guid.TryParse(x.EntityId, out _))
                .Select(x => Guid.Parse(x.EntityId))
                .Take(take)
                .ToList();
            if (orderedIds.Count == 0)
            {
                return new List<StudyGroup>();
            }

            var rows = await _db.StudyGroups.AsNoTracking()
                .Where(g => orderedIds.Contains(g.Id)
                    && (string.IsNullOrEmpty(g.Status)
                        || g.Status == "approved"
                        || g.Status == "Approved"
                        || g.Status == "active"
                        || g.Status == "Active"))
                .ToListAsync();

            return orderedIds
                .Select(id => rows.FirstOrDefault(row => row.Id == id))
                .Where(row => row != null)
                .Select(row => new StudyGroup
                {
                    Id = row!.Id.ToString(),
                    Name = row.Name,
                    Description = row.Description,
                    ImageUrl = row.ImageUrl,
                    Status = row.Status
                })
                .ToList();
        }

        private static List<T> MergeByKey<T>(
            IEnumerable<T> keywordItems,
            IEnumerable<T> vectorItems,
            Func<T, string> keySelector,
            int take)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var merged = new List<T>();

            foreach (var item in vectorItems.Concat(keywordItems))
            {
                var key = keySelector(item);
                if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                {
                    continue;
                }

                merged.Add(item);
                if (merged.Count >= take)
                {
                    break;
                }
            }

            return merged;
        }
    }
}
