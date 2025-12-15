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
using PdfSharp.Charting;

namespace Sciencetopia.Controllers.KnowledgeNetwork;

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
    public async Task<IActionResult> Get(string url, string? title = null)
    {
        var extractedUrl = NormalizeUrl(url);
        if (extractedUrl == null)
        {
            // Be tolerant: return minimal preview so FE can still render a clickable link
            return Ok(new { Title = title ?? url, Description = string.Empty, Image = string.Empty });
        }

        var client = _clientFactory.CreateClient();
        var response = await client.GetAsync(extractedUrl);
        if (!response.IsSuccessStatusCode)
        {
            // Return minimal preview instead of 4xx to keep UI functional
            return Ok(new { Title = title ?? extractedUrl.ToString(), Description = string.Empty, Image = string.Empty });
        }

        // If the content is PDF and title was not provided, extract from PDF
        if (response.Content.Headers.ContentType?.MediaType == "application/pdf")
        {
            var pdfStream = await response.Content.ReadAsStreamAsync();
            string pdfTitle = title ?? string.Empty;

            if (title == null)
            {
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
            }

            var pdfPreview = new
            {
                Title = pdfTitle
            };

            return Ok(pdfPreview);
        }

        // Handle HTML content
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

        string extractedTitle = title ??
            doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", string.Empty)
            ?? string.Empty;
        if (string.IsNullOrEmpty(extractedTitle) && title == null)
        {
            extractedTitle = doc.DocumentNode.SelectSingleNode("//title")?.InnerText ?? string.Empty;
        }

        var description = doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']")?.GetAttributeValue("content", string.Empty);
        if (string.IsNullOrEmpty(description))
        {
            description = doc.DocumentNode.SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content", string.Empty);
        }

        var image = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", string.Empty);

        var preview = new
        {
            Title = extractedTitle,
            Description = description,
            Image = image
        };

        return Ok(preview);
    }

    private Uri? NormalizeUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        try { s = Uri.UnescapeDataString(s); } catch { /* ignore */ }

        if (s.StartsWith("//")) s = "https:" + s;
        if (!s.Contains("://"))
        {
            // If looks like a domain or path, default to https
            if (Regex.IsMatch(s, @"^[\w.-]+(\.[\w.-]+)+(/.*)?$"))
            {
                s = "https://" + s;
            }
        }
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri)) return uri;
        return null;
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
            return string.Empty;
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
