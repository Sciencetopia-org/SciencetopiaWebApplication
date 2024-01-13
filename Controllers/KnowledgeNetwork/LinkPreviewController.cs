using System;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;

[Route("api/[controller]")]
[ApiController]
public class LinkPreviewController : ControllerBase
{
    private readonly IHttpClientFactory _clientFactory;

    public LinkPreviewController(IHttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Get(string url)
    {
        var client = _clientFactory.CreateClient();
        var response = await client.GetAsync(url);
        if (response.IsSuccessStatusCode)
        {
            var contentBytes = await response.Content.ReadAsByteArrayAsync();
            var utf8String = Encoding.UTF8.GetString(contentBytes);

            var doc = new HtmlDocument();
            doc.LoadHtml(utf8String);

            // 尝试从 HTML meta 标签获取字符集
            var metaCharset = doc.DocumentNode.SelectSingleNode("//meta[@http-equiv='Content-Type']")
                ?.GetAttributeValue("content", string.Empty);
            var charset = metaCharset?.Split("charset=")[1];

            // 如果找到特定字符集，则重新解析 HTML
            if (!string.IsNullOrEmpty(charset))
            {
                var correctEncoding = Encoding.GetEncoding(charset);
                var correctString = correctEncoding.GetString(contentBytes);
                doc = new HtmlDocument();
                doc.LoadHtml(correctString);
            }

            var title = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", string.Empty);
            if (string.IsNullOrEmpty(title))
            {
                // 尝试获取常规的 <title> 标签内容
                title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText;
            }

            var description = doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", string.Empty);
            if (string.IsNullOrEmpty(description))
            {
                // 尝试获取常规的 <meta name="description"> 标签内容
                description = doc.DocumentNode.SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content", string.Empty);
            }

            var image = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", string.Empty);
            // 图片可能需要更复杂的逻辑来确定合适的回退方案

            var preview = new
            {
                Title = title,
                Description = description,
                Image = image
            };

            return Ok(preview);
        }

        return NotFound();
    }
}
