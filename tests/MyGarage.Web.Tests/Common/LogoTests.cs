using Bunit;
using MyGarage.Web.Common;

namespace MyGarage.Web.Tests.Common;

public class LogoTests : BunitContext
{
    [Fact]
    public void ItIsADecorativeSvgThatTakesTheSurroundingColour()
    {
        var cut = Render<Logo>();

        var svg = cut.Find("svg.mg-logo");
        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        Assert.Equal("currentColor", svg.GetAttribute("stroke"));
        Assert.Equal("24", svg.GetAttribute("width"));
        Assert.Equal(string.Empty, svg.TextContent.Trim());
    }

    [Fact]
    public void ItTakesASize()
    {
        var cut = Render<Logo>(parameters => parameters.Add(logo => logo.Size, 36));

        Assert.Equal("36", cut.Find("svg.mg-logo").GetAttribute("height"));
    }
}
