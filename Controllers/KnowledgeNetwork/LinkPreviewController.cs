using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using HtmlAgilityPack;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

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
        var extractedUrl = ExtractURL(url);
        if (extractedUrl == null)
        {
            return BadRequest("No URL found in the text.");
        }

        var client = _clientFactory.CreateClient();
        var response = await client.GetAsync(extractedUrl);
        if (!response.IsSuccessStatusCode)
        {
            return NotFound("Failed to fetch the URL.");
        }

        if (response.Content.Headers.ContentType?.MediaType == "application/pdf")
        {
            var pdfStream = await response.Content.ReadAsStreamAsync();
            string pdfTitle;

            if (extractedUrl.AbsolutePath.EndsWith(".pdf"))
            {
                pdfTitle = await ExtractTitleFromPdfUsingTika(pdfStream);
            }
            else
            {
                pdfTitle = ExtractTitleFromPdfMetadata(pdfStream);
                if (string.IsNullOrEmpty(pdfTitle))
                {
                    pdfTitle = await ExtractTitleFromPdfUsingTika(pdfStream);
                }
            }

            var pdfPreview = new
            {
                Title = pdfTitle
            };

            return Ok(pdfPreview);
        }

        var contentBytes = await response.Content.ReadAsByteArrayAsync();
        var utf8String = Encoding.UTF8.GetString(contentBytes);

        var doc = new HtmlDocument();
        doc.LoadHtml(utf8String);

        var metaCharset = doc.DocumentNode.SelectSingleNode("//meta[@http-equiv='Content-Type']")
            ?.GetAttributeValue("content", string.Empty);
        var charset = metaCharset?.Split("charset=")[1];

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
            title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText;
        }

        var description = doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", string.Empty);
        if (string.IsNullOrEmpty(description))
        {
            description = doc.DocumentNode.SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content", string.Empty);
        }

        var image = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", string.Empty);

        var preview = new
        {
            Title = title,
            Description = description,
            Image = image
        };

        return Ok(preview);
    }

    private Uri? ExtractURL(string text)
    {
        var regex = new Regex(@"https?://\S+");
        var match = regex.Match(text);
        return match.Success ? new Uri(match.Value) : null;
    }

    private string ExtractTitleFromPdfMetadata(Stream pdfStream)
    {
        try
        {
            using (var memoryStream = new MemoryStream())
            {
                pdfStream.CopyTo(memoryStream);
                memoryStream.Position = 0;

                using (var document = PdfReader.Open(memoryStream, PdfDocumentOpenMode.Import))
                {
                    return document.Info.Title;
                }
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<string> ExtractTitleFromPdfUsingTika(Stream pdfStream)
    {
        using (var client = _clientFactory.CreateClient())
        {
            var content = new StreamContent(pdfStream);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");

            var response = await client.PutAsync("http://localhost:9998/tika", content);
            response.EnsureSuccessStatusCode();

            var text = await response.Content.ReadAsStringAsync();

            return ExtractTitleFromText(text);
        }
    }

    private string ExtractTitleFromText(string text)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(text);

        var titleNode = doc.DocumentNode.SelectSingleNode("//body//div[@class='page']//p/following-sibling::p[1]");
        return titleNode?.InnerText.Trim() ?? "Untitled PDF";
    }
}
