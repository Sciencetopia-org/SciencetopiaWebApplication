using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Sciencetopia.Controllers.KnowledgeNetwork;

[Route("api/[controller]")]
[ApiController]
public class LinkPreviewController : ControllerBase
{
    private const int MaxUrlLength = 2048;
    private const int MaxRedirects = 3;
    private const int MaxPreviewBytes = 256 * 1024;
    private readonly IHttpClientFactory _clientFactory;

    public LinkPreviewController(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    [HttpGet]
    [EnableRateLimiting("LinkPreview")]
    public async Task<IActionResult> Get(string url, string? title = null, CancellationToken ct = default)
    {
        var extractedUrl = await NormalizeAndValidateUrlAsync(url, ct);
        if (extractedUrl == null)
        {
            return BadRequest(new { message = "URL is not allowed for link preview." });
        }

        try
        {
            var response = await FetchWithValidatedRedirectsAsync(extractedUrl, ct);
            if (response == null)
            {
                return Ok(MinimalPreview(title ?? extractedUrl.ToString()));
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode || !IsHtmlContent(response.Content.Headers.ContentType))
                {
                    return Ok(MinimalPreview(title ?? extractedUrl.ToString()));
                }

                var contentBytes = await ReadLimitedAsync(response.Content, ct);
                if (contentBytes == null)
                {
                    return StatusCode(StatusCodes.Status413PayloadTooLarge, new { message = "Preview response is too large." });
                }

                var html = DecodeHtml(contentBytes, response.Content.Headers.ContentType);
                var preview = BuildPreview(html, response.RequestMessage?.RequestUri ?? extractedUrl, title);
                return Ok(preview);
            }
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new { message = "Preview request timed out." });
        }
        catch
        {
            return Ok(MinimalPreview(title ?? extractedUrl.ToString()));
        }
    }

    private async Task<HttpResponseMessage?> FetchWithValidatedRedirectsAsync(Uri initialUrl, CancellationToken ct)
    {
        var current = initialUrl;
        for (var redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            current = await NormalizeAndValidateUrlAsync(current.ToString(), ct) ?? throw new InvalidOperationException("Redirect URL is not allowed.");
            var client = _clientFactory.CreateClient("LinkPreview");
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("SciencetopiaLinkPreview/1.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));

            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location == null)
            {
                return null;
            }

            current = location.IsAbsoluteUri ? location : new Uri(current, location);
        }

        return null;
    }

    internal static async Task<Uri?> NormalizeAndValidateUrlAsync(string? text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var raw = text.Trim();
        if (raw.Length > MaxUrlLength || HasControlCharacter(raw)) return null;
        if (raw.StartsWith("//", StringComparison.Ordinal)) raw = "https:" + raw;
        if (!raw.Contains("://", StringComparison.Ordinal))
        {
            if (Regex.IsMatch(raw, @"^[A-Za-z0-9.-]+(\.[A-Za-z0-9.-]+)+(:[0-9]{1,5})?(/.*)?$"))
            {
                raw = "https://" + raw;
            }
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        if (string.IsNullOrWhiteSpace(uri.Host)) return null;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return null;
        if (uri.Port is <= 0 or > 65535) return null;
        if (IsForbiddenHostName(uri.Host)) return null;

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
        }
        catch
        {
            return null;
        }

        if (addresses.Length == 0 || addresses.Any(IsForbiddenAddress)) return null;
        return uri;
    }

    internal static bool IsForbiddenAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address)) return true;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var first = bytes[0];
            var second = bytes[1];
            return first == 0
                || first == 10
                || first == 127
                || (first == 100 && second is >= 64 and <= 127)
                || (first == 169 && second == 254)
                || (first == 172 && second is >= 16 and <= 31)
                || (first == 192 && second == 168)
                || (first == 192 && second == 0)
                || (first == 192 && second == 0 && bytes[2] == 2)
                || (first == 198 && second is 18 or 19)
                || (first == 203 && second == 0 && bytes[2] == 113)
                || first >= 224
                || address.Equals(IPAddress.Parse("169.254.169.254"));
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.Equals(IPAddress.IPv6None)
                || address.Equals(IPAddress.IPv6Loopback)
                || address.IsIPv6LinkLocal
                || address.IsIPv6Multicast
                || (bytes[0] & 0xfe) == 0xfc;
        }

        return true;
    }

    private static bool IsForbiddenHostName(string host)
    {
        var normalized = host.TrimEnd('.').ToLowerInvariant();
        return normalized == "localhost"
            || normalized == "host.docker.internal"
            || normalized.EndsWith(".local", StringComparison.Ordinal)
            || normalized.EndsWith(".internal", StringComparison.Ordinal)
            || normalized.EndsWith(".svc", StringComparison.Ordinal)
            || normalized.EndsWith(".cluster.local", StringComparison.Ordinal)
            || normalized is "metadata.google.internal" or "169.254.169.254";
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.Moved
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static bool IsHtmlContent(MediaTypeHeaderValue? contentType)
    {
        var mediaType = contentType?.MediaType;
        return string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]?> ReadLimitedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), ct)) > 0)
        {
            if (buffer.Length + read > MaxPreviewBytes) return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string DecodeHtml(byte[] contentBytes, MediaTypeHeaderValue? contentType)
    {
        var charset = contentType?.CharSet;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { return Encoding.GetEncoding(charset.Trim('"')).GetString(contentBytes); } catch { }
        }

        return Encoding.UTF8.GetString(contentBytes);
    }

    private static object BuildPreview(string html, Uri baseUri, string? titleOverride)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var extractedTitle = titleOverride
            ?? ReadMeta(doc, "og:title")
            ?? CleanText(doc.DocumentNode.SelectSingleNode("//title")?.InnerText, 200);
        var description = ReadMeta(doc, "og:description")
            ?? ReadNamedMeta(doc, "description");
        var image = ResolveSafeRemoteUrl(ReadMeta(doc, "og:image"), baseUri);

        return new
        {
            Title = CleanText(extractedTitle, 200),
            Description = CleanText(description, 500),
            Image = image
        };
    }

    private static object MinimalPreview(string title)
        => new { Title = CleanText(title, 200), Description = string.Empty, Image = string.Empty };

    private static string? ReadMeta(HtmlDocument doc, string property)
        => doc.DocumentNode.SelectSingleNode($"//meta[@property='{property}']")?.GetAttributeValue("content", string.Empty);

    private static string? ReadNamedMeta(HtmlDocument doc, string name)
        => doc.DocumentNode.SelectSingleNode($"//meta[@name='{name}']")?.GetAttributeValue("content", string.Empty);

    private static string ResolveSafeRemoteUrl(string? value, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (!Uri.TryCreate(baseUri, value, out var uri)) return string.Empty;
        return uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) ? uri.ToString() : string.Empty;
    }

    private static string CleanText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var cleaned = WebUtility.HtmlDecode(value)
            .Where(ch => !char.IsControl(ch) || ch is '\r' or '\n' or '\t')
            .Aggregate(new StringBuilder(), (builder, ch) => builder.Append(ch))
            .ToString()
            .Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    private static bool HasControlCharacter(string value)
        => value.Any(ch => char.IsControl(ch));
}
