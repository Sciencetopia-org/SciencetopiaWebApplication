using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;

namespace Sciencetopia.Services.Region;

public class RegionService : IRegionService
{
    private static readonly HttpClient Http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    // Basic in-memory cache by IP for a short duration
    private static readonly ConcurrentDictionary<string, (bool IsCn, DateTimeOffset ExpireAt)> Cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    public async Task<bool> IsMainlandChinaAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        try
        {
            var ip = GetClientIp(httpContext);
            if (string.IsNullOrWhiteSpace(ip)) return false;

            if (Cache.TryGetValue(ip, out var entry))
            {
                if (entry.ExpireAt > DateTimeOffset.UtcNow) return entry.IsCn;
            }

            // Prefer ipapi.co (HTTPS), fallback to ipwho.is
            var isCn = await CheckIpApiCoAsync(ip, cancellationToken)
                      ?? await CheckIpWhoAsync(ip, cancellationToken)
                      ?? false;

            Cache[ip] = (isCn, DateTimeOffset.UtcNow.Add(CacheTtl));
            return isCn;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetClientIp(HttpContext ctx)
    {
        var xff = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(xff))
        {
            var first = xff.Split(',').Select(x => x.Trim()).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first)) return first;
        }
        return ctx.Connection.RemoteIpAddress?.ToString();
    }

    private static async Task<bool?> CheckIpApiCoAsync(string ip, CancellationToken ct)
    {
        try
        {
            // https://ipapi.co/{ip}/country/ returns 2-letter code like "CN"
            var url = $"https://ipapi.co/{ip}/country/";
            var resp = await Http.GetStringAsync(url, ct);
            if (string.IsNullOrWhiteSpace(resp)) return null;
            return resp.Trim().Equals("CN", StringComparison.OrdinalIgnoreCase);
        }
        catch { return null; }
    }

    private static async Task<bool?> CheckIpWhoAsync(string ip, CancellationToken ct)
    {
        try
        {
            // https://ipwho.is/{ip}
            var url = $"https://ipwho.is/{ip}";
            using var resp = await Http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var doc = await resp.Content.ReadFromJsonAsync<IpWhoDoc>(cancellationToken: ct);
            if (doc == null) return null;
            return string.Equals(doc.country_code, "CN", StringComparison.OrdinalIgnoreCase) && doc.success;
        }
        catch { return null; }
    }

    private record IpWhoDoc(bool success, string country_code);
}

