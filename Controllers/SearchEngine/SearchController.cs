using Microsoft.AspNetCore.Mvc;
using Neo4j.Driver;
using Sciencetopia.Data;
using Sciencetopia.Services;

namespace Sciencetopia.Controllers.SearchEngine
{
    [ApiController]
    [Route("api/[controller]")]
    public class SearchController : ControllerBase
    {
        private readonly IKnowledgeNodeRepository _knowledgeRepo;
        private readonly SearchService _searchService;
        private readonly StudyGroupService _studyGroupService;

        public SearchController(
            IKnowledgeNodeRepository knowledgeRepo,
            SearchService searchService,
            StudyGroupService studyGroupService)
        {
            _knowledgeRepo = knowledgeRepo;
            _searchService = searchService;
            _studyGroupService = studyGroupService;
        }

        [HttpGet]
        public async Task<IActionResult> SearchAsync(string query, int page = 1, int pageSize = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query parameter is required.");

            var skip = (page - 1) * pageSize;

            // Run sequentially to avoid concurrent DbContext usage across scoped repositories
            var knowledgeBase = await _knowledgeRepo.SearchKnowledgeNodesAsync(query, skip, pageSize);
            var resources = await _searchService.SearchResourcesWithLinkedNodesAsync(query, skip, pageSize);
            var studyGroups = await _studyGroupService.SearchStudyGroups(query, skip, pageSize);

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
            return Ok(result);
        }

        [HttpGet("SearchResources")]
        public async Task<IActionResult> SearchResourcesAsync(string query, int page = 1, int pageSize = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query parameter is required.");

            var skip = (page - 1) * pageSize;
            var result = await _searchService.SearchResourcesWithLinkedNodesAsync(query, skip, pageSize);
            return Ok(result);
        }

        [HttpGet("SearchStudyGroups")]
        public async Task<IActionResult> SearchStudyGroupsAsync(string query, int page = 1, int pageSize = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest("Query parameter is required.");

            var skip = (page - 1) * pageSize;
            var result = await _studyGroupService.SearchStudyGroups(query, skip, pageSize);
            return Ok(result);
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
    }
}
