using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Components.Layout;

namespace MyGarage.Web.Tests.Components.Layout;

public class MainLayoutTests : BunitContext
{
    public MainLayoutTests()
    {
        Services.AddSingleton<AntiforgeryStateProvider, FakeAntiforgeryStateProvider>();
        AddAuthorization();
    }

    private IRenderedComponent<MainLayout> RenderLayout() =>
        Render<MainLayout>(ps => ps.Add(p => p.Body, "<p id=\"page-body\">Hello</p>"));

    [Fact]
    public void SkipLinkIsTheFirstFocusableElement()
    {
        var cut = RenderLayout();

        var first = cut.Find("a, button, input, select, textarea");
        Assert.Equal("#main-content", first.GetAttribute("href"));
        Assert.Equal("Skip to main content", first.TextContent.Trim());
    }

    [Fact]
    public void NavbarPrecedesTheMainContentArea()
    {
        var cut = RenderLayout();
        var markup = cut.Markup;

        Assert.True(markup.IndexOf("fixed-top", StringComparison.Ordinal) < markup.IndexOf("id=\"main-content\"", StringComparison.Ordinal));
    }

    [Fact]
    public void BodyRendersInAFullWidthMainElement()
    {
        var cut = RenderLayout();

        var main = cut.Find("main#main-content");
        Assert.Equal("-1", main.GetAttribute("tabindex"));
        Assert.Contains("container-fluid", main.ClassName);
        var classes = main.ClassName!.Split(' ');
        Assert.DoesNotContain("container", classes);
        Assert.NotNull(main.QuerySelector("#page-body"));
    }

    [Fact]
    public void KeepsTheBlazorErrorUi()
    {
        var cut = RenderLayout();

        Assert.NotNull(cut.Find("#blazor-error-ui"));
    }
}
