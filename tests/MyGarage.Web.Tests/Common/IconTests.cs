using Bunit;
using MyGarage.Web.Common;

namespace MyGarage.Web.Tests.Common;

public class IconTests : BunitContext
{
    [Theory]
    [InlineData(IconName.Search)]
    [InlineData(IconName.Bell)]
    [InlineData(IconName.QuestionCircle)]
    [InlineData(IconName.PersonCircle)]
    public void RendersDecorativeInlineSvg(IconName name)
    {
        var cut = Render<Icon>(ps => ps.Add(p => p.Name, name));

        var svg = cut.Find("svg");
        Assert.Equal("bi", svg.GetAttribute("class"));
        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        Assert.Equal("false", svg.GetAttribute("focusable"));
        Assert.Equal("currentColor", svg.GetAttribute("fill"));
        Assert.False(string.IsNullOrWhiteSpace(svg.GetAttribute("viewBox")));
        Assert.NotEmpty(cut.FindAll("path"));
        Assert.Equal(string.Empty, svg.TextContent.Trim());
    }

    [Fact]
    public void DefaultSizeIsSixteen()
    {
        var cut = Render<Icon>(ps => ps.Add(p => p.Name, IconName.Bell));

        var svg = cut.Find("svg");
        Assert.Equal("16", svg.GetAttribute("width"));
        Assert.Equal("16", svg.GetAttribute("height"));
    }

    [Fact]
    public void SizeParameterChangesWidthAndHeight()
    {
        var cut = Render<Icon>(ps => ps.Add(p => p.Name, IconName.Bell).Add(p => p.Size, 24));

        var svg = cut.Find("svg");
        Assert.Equal("24", svg.GetAttribute("width"));
        Assert.Equal("24", svg.GetAttribute("height"));
    }
}
