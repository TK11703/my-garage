using System.Net;
using System.Xml.Linq;
using MyGarage.Web.Tests.Common;

namespace MyGarage.Web.Tests.Common;

public class BrandAssetTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory factory;

    public BrandAssetTests(WebAppFactory factory) => this.factory = factory;

    [Theory]
    [InlineData("/images/logo-mark-on-dark.svg")]
    [InlineData("/images/logo-mark-on-light.svg")]
    [InlineData("/images/favicon.svg")]
    public async Task SvgAsset_IsServedAnonymouslyAndIsSafe(string path)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length < 5120, $"{path} is {bytes.Length} bytes.");

        var root = XDocument.Parse(System.Text.Encoding.UTF8.GetString(bytes)).Root!;
        Assert.Equal("svg", root.Name.LocalName);
        Assert.NotNull(root.Attribute("viewBox"));

        var all = root.DescendantsAndSelf().ToList();
        Assert.DoesNotContain(all, element => element.Name.LocalName.Equals("script", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(all, element => element.Attributes().Any(attribute =>
            attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
            || attribute.Name.LocalName.Equals("href", StringComparison.OrdinalIgnoreCase)));
    }
}
