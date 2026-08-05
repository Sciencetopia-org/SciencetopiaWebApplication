using System.Net;
using Sciencetopia.Controllers.KnowledgeNetwork;

namespace SciencetopiaWebApplication.Tests;

public class LinkPreviewSecurityTests
{
    [Theory]
    [InlineData("http://localhost/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/file")]
    [InlineData("https://user:pass@example.com/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    public async Task Rejects_Disallowed_Urls(string url)
    {
        var result = await LinkPreviewController.NormalizeAndValidateUrlAsync(url);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    public void Rejects_Private_And_Local_Addresses(string address)
    {
        Assert.True(LinkPreviewController.IsForbiddenAddress(IPAddress.Parse(address)));
    }

    [Fact]
    public void Accepts_Normal_Public_Address_Check()
    {
        Assert.False(LinkPreviewController.IsForbiddenAddress(IPAddress.Parse("93.184.216.34")));
    }
}
