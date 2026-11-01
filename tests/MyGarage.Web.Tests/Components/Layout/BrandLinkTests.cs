using Bunit;
using MyGarage.Web.Components.Layout;

namespace MyGarage.Web.Tests.Components.Layout;

public class BrandLinkTests : BunitContext
{
    [Fact]
    public void RendersLogoMarkAndTheNameAsText()
    {
        var cut = Render<BrandLink>();

        var link = cut.Find("a.navbar-brand");
        Assert.Equal("/", link.GetAttribute("href"));

        var image = link.QuerySelector("img")!;
        Assert.EndsWith("images/logo-mark-on-dark.svg", image.GetAttribute("src"));
        Assert.Equal(string.Empty, image.GetAttribute("alt"));
        Assert.Equal("32", image.GetAttribute("width"));
        Assert.Equal("32", image.GetAttribute("height"));

        Assert.Equal("My Garage", link.TextContent.Trim());
    }
}
