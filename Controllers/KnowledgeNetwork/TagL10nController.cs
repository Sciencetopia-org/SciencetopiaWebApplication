using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Services.L10n;

namespace Sciencetopia.Controllers.KnowledgeNetwork
{
    [ApiController]
    [Route("api/tags")]
    public class TagL10nController : ControllerBase
    {
        private readonly IL10nService _l10n;
        public TagL10nController(IL10nService l10n) { _l10n = l10n; }

        [HttpGet("{id:guid}/l10n")]
        public async Task<ActionResult<string?>> GetLocalized(Guid id, [FromQuery] string field = "title", [FromQuery] string? lang = null)
        {
            var value = await _l10n.GetLocalizedForTagAsync(id, field, lang);
            return Ok(value);
        }

        [HttpGet("{id:guid}/l10n/items")]
        public async Task<ActionResult<IEnumerable<L10nItemDto>>> List(Guid id, [FromQuery] string field = "title")
        {
            var items = await _l10n.ListForTagAsync(id, field);
            return Ok(items);
        }

        public record UpsertRequest(string fieldKey, string lang, string value, bool isLongText = false, bool primary = true, int? sortOrder = null);

        [HttpPost("{id:guid}/l10n")]
        [Authorize(Roles = "administrator")]
        public async Task<ActionResult<Guid>> Upsert(Guid id, [FromBody] UpsertRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.fieldKey) || string.IsNullOrWhiteSpace(req.lang))
                return BadRequest("fieldKey and lang are required");
            var result = await _l10n.UpsertForTagAsync(id, req.fieldKey, req.lang, req.value, req.isLongText, req.primary, req.sortOrder);
            return Ok(result);
        }

        [HttpDelete("{id:guid}/l10n/{itemId:guid}")]
        [Authorize(Roles = "administrator")]
        public async Task<IActionResult> Delete(Guid id, Guid itemId)
        {
            await _l10n.RemoveForTagAsync(id, itemId);
            return NoContent();
        }
    }
}

