using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Services.KnowledgeGraph;

namespace Sciencetopia.Controllers.Admin;

[ApiController]
[Route("api/Admin/KnowledgeGraph")] 
[Authorize(Policy = "RequireAdministratorRole")]
public sealed class KnowledgeGraphSyncController : ControllerBase
{
    private readonly IGraphSyncService _sync;
    public KnowledgeGraphSyncController(IGraphSyncService sync) => _sync = sync;

    [HttpPost("SyncLatest")]
    public async Task<IActionResult> SyncLatest()
    {
        var (nodes, tags) = await _sync.SyncAllAsync();
        return Ok(new { nodes, tags });
    }
}

