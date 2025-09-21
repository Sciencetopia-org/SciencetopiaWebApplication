using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Middleware;
using Sciencetopia.Services.L10n;

namespace SciencetopiaWebApplication.Controllers
{
    [ApiController]
    [Route("api/l10n")] 
    public class L10nUnifiedController : ControllerBase
    {
        private readonly IL10nService _l10n;
        private readonly ILanguageContext _langCtx;
        public L10nUnifiedController(IL10nService l10n, ILanguageContext langCtx)
        {
            _l10n = l10n; _langCtx = langCtx;
        }

        [HttpGet("{scope}/{id:guid}")]
        public async Task<ActionResult<string?>> Get(string scope, Guid id, [FromQuery] string field = "name", [FromQuery] string? lang = null)
        {
            lang ??= _langCtx.EffectiveLang;
            string? value = scope switch
            {
                "knowledge_node" => await _l10n.GetLocalizedAsync(id, field, lang),
                "tag" => await _l10n.GetLocalizedForTagAsync(id, field, lang),
                _ => null
            };
            return Ok(value);
        }

        [HttpGet("{scope}/{id:guid}/items")]
        public async Task<ActionResult<IEnumerable<L10nItemDto>>> List(string scope, Guid id, [FromQuery] string field = "name")
        {
            var items = scope switch
            {
                "knowledge_node" => await _l10n.ListAsync(id, field),
                "tag" => await _l10n.ListForTagAsync(id, field),
                _ => Array.Empty<L10nItemDto>()
            };
            return Ok(items);
        }

        public record L10nUnifiedUpsertRequest(string fieldKey, string lang, string value, bool isLongText = false, bool primary = true, int? sortOrder = null);

        [HttpPost("{scope}/{id:guid}")]
        [Authorize(Roles = "administrator")]
        public async Task<ActionResult<Guid>> Upsert(string scope, Guid id, [FromBody] L10nUnifiedUpsertRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.fieldKey) || string.IsNullOrWhiteSpace(req.lang))
                return BadRequest("fieldKey and lang are required");
            Guid result = scope switch
            {
                "knowledge_node" => await _l10n.UpsertAsync(id, req.fieldKey, req.lang, req.value, req.isLongText, req.primary, req.sortOrder),
                "tag" => await _l10n.UpsertForTagAsync(id, req.fieldKey, req.lang, req.value, req.isLongText, req.primary, req.sortOrder),
                _ => Guid.Empty
            };
            if (result == Guid.Empty) return BadRequest("Invalid scope");
            return Ok(result);
        }

        [HttpDelete("{scope}/{id:guid}/{itemId:guid}")]
        [Authorize(Roles = "administrator")]
        public async Task<IActionResult> Delete(string scope, Guid id, Guid itemId)
        {
            switch (scope)
            {
                case "knowledge_node":
                    await _l10n.RemoveAsync(id, itemId); break;
                case "tag":
                    await _l10n.RemoveForTagAsync(id, itemId); break;
                default:
                    return BadRequest("Invalid scope");
            }
            return NoContent();
        }
    }
}
