using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Sciencetopia.Services.KnowledgeGraph;

namespace Sciencetopia.Controllers.KnowledgeNetwork;

[ApiController]
[Route("knowledge")]
public sealed class KnowledgeVersioningController : ControllerBase
{
    private readonly IVersioningService _versioning;
    private readonly IGraphRepository _graphRepo;
    public KnowledgeVersioningController(IVersioningService versioning, IGraphRepository graphRepo)
    {
        _versioning = versioning;
        _graphRepo = graphRepo;
    }

    // Nodes
    [HttpGet("nodes/{stableId:guid}/versions")]
    public async Task<IActionResult> GetNodeVersions(Guid stableId)
        => Ok(await _versioning.GetNodeVersionsAsync(stableId));

    [HttpPost("nodes/{stableId:guid}/draft")]
    [Authorize]
    public async Task<IActionResult> CreateNodeDraft(Guid stableId, [FromBody] CreateNodeDraftBody body)
    {
        var uid = User?.Identity?.Name ?? "system";
        var id = await _versioning.CreateNodeDraftAsync(stableId, n =>
        {
            if (body != null)
            {
                n.Name = body.Name ?? n.Name;
                n.Description = body.Description ?? n.Description;
            }
        }, uid);
        return Ok(new { versionId = id });
    }

    [HttpPost("nodes/publish/{draftVersionId:guid}")]
    [Authorize]
    public async Task<IActionResult> PublishNode(Guid draftVersionId)
    {
        var approver = User?.Identity?.Name ?? "system";
        await _versioning.PublishNodeAsync(draftVersionId, approver);
        return Ok();
    }

    // Tags
    [HttpGet("tags/{stableId:guid}/versions")]
    public async Task<IActionResult> GetTagVersions(Guid stableId)
        => Ok(await _versioning.GetTagVersionsAsync(stableId));

    [HttpPost("tags/{stableId:guid}/draft")]
    [Authorize]
    public async Task<IActionResult> CreateTagDraft(Guid stableId, [FromBody] CreateTagDraftBody body)
    {
        var uid = User?.Identity?.Name ?? "system";
        var id = await _versioning.CreateTagDraftAsync(stableId, t =>
        {
            if (body != null)
            {
                t.Name = body.Name ?? t.Name;
                t.Description = body.Description ?? t.Description;
            }
        }, uid);
        return Ok(new { versionId = id });
    }

    [HttpPost("tags/publish/{draftVersionId:guid}")]
    [Authorize]
    public async Task<IActionResult> PublishTag(Guid draftVersionId)
    {
        var approver = User?.Identity?.Name ?? "system";
        await _versioning.PublishTagAsync(draftVersionId, approver);
        return Ok();
    }

    // Graph views
    [HttpGet("graph/active")]
    public async Task<IActionResult> GetActiveGraph([FromQuery] DateTimeOffset? asOf = null)
        => Ok(await _graphRepo.GetActiveGraphAsync(asOf));

    [HttpGet("graph/proposals")]
    public async Task<IActionResult> GetProposals()
        => Ok(await _graphRepo.GetProposalsAsync());

    public sealed record CreateNodeDraftBody(string? Name, string? Description);
    public sealed record CreateTagDraftBody(string? Name, string? Description);
}

