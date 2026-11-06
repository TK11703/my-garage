using Bunit;
using MyGarage.Web.Common;

namespace MyGarage.Web.Tests.Common;

public class HighlightTests : BunitContext
{
    private static string Text(IRenderedComponent<Highlight> cut) => string.Concat(cut.Nodes.Select(node => node.TextContent));

    [Fact]
    public void WrapsEveryCaseInsensitiveMatchInAMark()
    {
        var cut = Render<Highlight>(parameters => parameters.Add(p => p.Text, "Ford F-150 ford").Add(p => p.Term, "ford"));

        Assert.Equal(["Ford", "ford"], cut.FindAll("mark").Select(mark => mark.TextContent));
        Assert.Equal("Ford F-150 ford", Text(cut));
    }

    [Fact]
    public void WithoutATerm_ShowsPlainText()
    {
        var cut = Render<Highlight>(parameters => parameters.Add(p => p.Text, "Ford"));

        Assert.Empty(cut.FindAll("mark"));
        Assert.Equal("Ford", Text(cut));
    }

    [Fact]
    public void EncodesMarkupInTheTextAndTheTerm()
    {
        var cut = Render<Highlight>(parameters => parameters.Add(p => p.Text, "a <b>bold</b> move").Add(p => p.Term, "<b>"));

        Assert.Empty(cut.FindAll("b"));
        Assert.Equal("<b>", cut.Find("mark").TextContent);
        Assert.Contains("&lt;/b&gt;", cut.Markup);
    }

    [Fact]
    public void LongText_IsCutAroundTheFirstMatchWithEllipses()
    {
        var text = new string('x', 100) + "tow" + new string('y', 100);

        var cut = Render<Highlight>(parameters => parameters.Add(p => p.Text, text).Add(p => p.Term, "tow").Add(p => p.Context, 10));

        Assert.Equal("\u2026" + new string('x', 10) + "tow" + new string('y', 10) + "\u2026", Text(cut));
    }
}
