using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Neo4j.Driver;

namespace SciencetopiaWebApplication.Controllers.StudyPlan
{
    [ApiController]
    [Route("api/[controller]")]
    public class StudyPlanTagsController : ControllerBase
    {
        private readonly StudyPlanService _service;
        private readonly ITagRepository _tagRepo;
        private readonly ITagResolutionService _tagResolution;
        private readonly ILogger<StudyPlanTagsController> _logger;

        public StudyPlanTagsController(
            StudyPlanService service,
            ITagRepository tagRepo,
            ITagResolutionService tagResolution,
            ILogger<StudyPlanTagsController> logger)
        {
            _service = service;
            _tagRepo = tagRepo;
            _tagResolution = tagResolution;
            _logger = logger;
        }

        public class UpdateTagsRequest
        {
            public List<Guid>? TagIds { get; set; }
            public List<string>? NewTagNames { get; set; }
        }

        [HttpGet("{planId}/Tags")]
        public async Task<IActionResult> GetPlanTags([FromRoute] string planId)
        {
            var tags = await _service.GetPlanTagsAsync(planId);
            // Resolve names for IDs
            var ids = tags.Where(t => t.Id.HasValue).Select(t => t.Id!.Value).ToList();
            var names = ids.Count > 0 ? await _tagRepo.GetTagNamesAsync(ids) : new Dictionary<Guid, string>();
            foreach (var t in tags) if (t.Id.HasValue && names.TryGetValue(t.Id.Value, out var nm)) t.Name = nm;
            return Ok(tags);
        }

        [HttpPost("{planId}/Tags")]
        [Authorize]
        public async Task<IActionResult> UpdatePlanTags([FromRoute] string planId, [FromBody] UpdateTagsRequest req)
        {
            var uid = User?.Identity?.Name ?? string.Empty;
            var tagIds = (req.TagIds ?? new List<Guid>()).Where(x => x != Guid.Empty).ToList();
            var newNames = (req.NewTagNames ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            if (newNames.Count > 0)
            {
                var (resolved, _) = await _tagResolution.ResolveOrCreateAsync(newNames, uid);
                tagIds.AddRange(resolved);
            }
            tagIds = tagIds.Distinct().ToList();
            var ok = await _service.UpdatePlanTagsAsync(planId, tagIds, null, uid);
            return ok ? Ok() : BadRequest();
        }

        [HttpGet("{planId}/Lessons/{lessonId}/Tags")]
        public async Task<IActionResult> GetLessonTags([FromRoute] string planId, [FromRoute] string lessonId)
        {
            var tags = await _service.GetLessonTagsAsync(planId, lessonId);
            var ids = tags.Where(t => t.Id.HasValue).Select(t => t.Id!.Value).ToList();
            var names = ids.Count > 0 ? await _tagRepo.GetTagNamesAsync(ids) : new Dictionary<Guid, string>();
            foreach (var t in tags) if (t.Id.HasValue && names.TryGetValue(t.Id.Value, out var nm)) t.Name = nm;
            return Ok(tags);
        }

        [HttpPost("{planId}/Lessons/{lessonId}/Tags")]
        [Authorize]
        public async Task<IActionResult> UpdateLessonTags([FromRoute] string planId, [FromRoute] string lessonId, [FromBody] UpdateTagsRequest req)
        {
            var uid = User?.Identity?.Name ?? string.Empty;
            var tagIds = (req.TagIds ?? new List<Guid>()).Where(x => x != Guid.Empty).ToList();
            var newNames = (req.NewTagNames ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            if (newNames.Count > 0)
            {
                var (resolved, _) = await _tagResolution.ResolveOrCreateAsync(newNames, uid);
                tagIds.AddRange(resolved);
            }
            tagIds = tagIds.Distinct().ToList();
            var ok = await _service.UpdateLessonTagsAsync(planId, lessonId, tagIds, null, uid);
            return ok ? Ok() : BadRequest();
        }

        [HttpGet("{planId}/Lessons/{lessonId}/SuggestTags")]
        public async Task<IActionResult> SuggestLessonTags([FromRoute] string planId, [FromRoute] string lessonId)
        {
            var tags = await _service.SuggestLessonTagsAsync(planId, lessonId);
            return Ok(tags);
        }

        [HttpGet("{planId}/SuggestTags")]
        public async Task<IActionResult> SuggestPlanTags([FromRoute] string planId)
        {
            var tags = await _service.SuggestPlanTagsAsync(planId);
            return Ok(tags);
        }

        public class SuggestFromPayloadRequest
        {
            public StudyPlanDTO? Payload { get; set; }
        }

        public class LessonSuggestion
        {
            public string? LessonId { get; set; }
            public string? Key { get; set; }
            public List<TagDTO> Tags { get; set; } = new();
        }

        public class SuggestFromPayloadResponse
        {
            public List<TagDTO> PlanTags { get; set; } = new();
            public List<LessonSuggestion> Lessons { get; set; } = new();
        }

        [HttpPost("SuggestFromPayload")]
        public async Task<IActionResult> SuggestFromPayload([FromBody] SuggestFromPayloadRequest req)
        {
            if (req?.Payload?.StudyPlan == null)
                return BadRequest("Payload missing studyPlan.");
            var result = await _service.SuggestFromPayloadAsync(req.Payload!);
            return Ok(result);
        }
    }
}
