using Microsoft.AspNetCore.Http;

namespace Sciencetopia.Services.Region;

public interface IRegionService
{
    Task<bool> IsMainlandChinaAsync(HttpContext httpContext, CancellationToken cancellationToken = default);
}

