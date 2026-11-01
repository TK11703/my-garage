using Bunit;
using MyGarage.Web.Components.Layout;

namespace MyGarage.Web.Tests.Components.Layout;

public class PageHeaderTests : BunitContext
{
    private IRenderedComponent<PageHeader> RenderHeader(string title, IReadOnlyList<Crumb>? trail = null, string? actions = null) =>
        Render<PageHeader>(ps =>
        {
            ps.Add(p => p.Title, title);
            if (trail is not null)
            {
                ps.Add(p => p.Trail, trail);
            }

            if (actions is not null)
            {
                ps.Add(p => p.Actions, actions);
            }
        });

    [Fact]
    public void EmptyTrail_ShowsHomeAsCurrentPageNotALink()
    {
        var cut = RenderHeader("Welcome");

        var items = cut.FindAll("nav[aria-label='Breadcrumb'] ol.breadcrumb > li");
        var home = Assert.Single(items);
        Assert.Equal("Home", home.TextContent.Trim());
        Assert.Equal("page", home.GetAttribute("aria-current"));
        Assert.Empty(home.QuerySelectorAll("a"));
    }

    [Fact]
    public void Trail_LinksHomeAndMarksLastCrumbCurrent()
    {
        var cut = RenderHeader("My Vehicles", [new Crumb("Vehicles")]);

        var items = cut.FindAll("ol.breadcrumb > li");
        Assert.Equal(2, items.Count);
        Assert.Equal("/", items[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("Vehicles", items[1].TextContent.Trim());
        Assert.Equal("page", items[1].GetAttribute("aria-current"));
        Assert.Empty(items[1].QuerySelectorAll("a"));
        Assert.Null(items[0].GetAttribute("aria-current"));
    }

    [Fact]
    public void Trail_LinksEarlierCrumbsAndOnlyLastIsCurrent()
    {
        var cut = RenderHeader("Civic", [new Crumb("Vehicles", "/vehicles"), new Crumb("Civic")]);

        var items = cut.FindAll("ol.breadcrumb > li");
        Assert.Equal(3, items.Count);
        Assert.Equal("/vehicles", items[1].QuerySelector("a")!.GetAttribute("href"));
        Assert.Null(items[1].GetAttribute("aria-current"));
        Assert.Equal("page", items[2].GetAttribute("aria-current"));
    }

    [Fact]
    public void Trail_IgnoresHrefOnTheLastCrumb()
    {
        var cut = RenderHeader("Edit", [new Crumb("Vehicles", "/vehicles"), new Crumb("Edit", "/somewhere")]);

        var last = cut.FindAll("ol.breadcrumb > li").Last();
        Assert.Empty(last.QuerySelectorAll("a"));
    }

    [Fact]
    public void RendersExactlyOneH1WithTheTitle()
    {
        var cut = RenderHeader("My Vehicles", [new Crumb("Vehicles")]);

        var h1 = Assert.Single(cut.FindAll("h1"));
        Assert.Equal("My Vehicles", h1.TextContent.Trim());
    }

    [Fact]
    public void Actions_RenderInsideTheTitleRowAfterTheHeading()
    {
        var cut = RenderHeader("My Vehicles", [new Crumb("Vehicles")], "<a class=\"btn btn-primary\" href=\"/vehicles/new\">Add vehicle</a>");

        var row = cut.Find("h1").ParentElement!;
        Assert.Equal("a", row.Children.Last().TagName.ToLowerInvariant());
        Assert.Equal("Add vehicle", row.QuerySelector("a.btn")!.TextContent);
    }

    [Fact]
    public void CrumbLabels_AreEncoded()
    {
        var cut = RenderHeader("Title", [new Crumb("<script>alert(1)</script>")]);

        Assert.DoesNotContain("<script>", cut.Markup);
        Assert.Contains("&lt;script&gt;", cut.Markup);
    }

    [Fact]
    public void CrumbLabels_CanTruncate()
    {
        var cut = RenderHeader("Title", [new Crumb("A very long vehicle nickname")]);

        Assert.NotNull(cut.Find("ol.breadcrumb > li:last-child .text-truncate"));
    }

    [Fact]
    public void BlankTitle_Throws()
    {
        Assert.Throws<ArgumentException>(() => RenderHeader(" "));
    }

    [Fact]
    public void BlankCrumbLabel_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Crumb(" "));
    }
}
