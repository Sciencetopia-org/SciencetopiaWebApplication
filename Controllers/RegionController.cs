using Microsoft.AspNetCore.Mvc;
using Sciencetopia.Services.Region;

namespace Sciencetopia.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RegionController : ControllerBase
{
    private readonly IRegionService _region;

    public RegionController(IRegionService region)
    {
        _region = region;
    }

    [HttpGet("Me")]
    public async Task<IActionResult> Me()
    {
        var isCn = await _region.IsMainlandChinaAsync(HttpContext);
        return Ok(new { isMainlandChina = isCn });
    }
}

