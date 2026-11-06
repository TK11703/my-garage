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
    public void EmptyTrail_ShowsNoBreadcrumb()
    {
        var cut = RenderHeader("Welcome");

        Assert.Empty(cut.FindAll("nav[aria-label='Breadcrumb']"));
    }

    [Fact]
    public void Trail_ShowsHomeAsALink_ThenTheMenuGroup_AndNeverTheCurrentPage()
    {
        var cut = RenderHeader("My Vehicles", [new Crumb("Vehicles")]);

        var items = cut.FindAll("nav[aria-label='Breadcrumb'] ol.breadcrumb > li");
        Assert.Equal(["Home", "Garage"], items.Select(item => item.TextContent.Trim()));
        Assert.Equal("/", items[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Empty(items[1].QuerySelectorAll("a"));
    }

    [Theory]
    [InlineData("Vehicles", "Garage")]
    [InlineData("Maintenance Drafts", "Maintenance")]
    [InlineData("FAQ", "Help")]
    [InlineData("About", "Help")]
    public void TheGroupIsFoundFromTheFirstCrumb(string firstCrumb, string group)
    {
        var cut = RenderHeader("Title", [new Crumb(firstCrumb)]);

        Assert.Equal(["Home", group], cut.FindAll("ol.breadcrumb > li").Select(item => item.TextContent.Trim()));
    }

    [Fact]
    public void APageOutsideTheMenuGroups_ShowsNoGroup()
    {
        var cut = RenderHeader("Preferences", [new Crumb("Preferences")]);

        Assert.Equal(["Home"], cut.FindAll("ol.breadcrumb > li").Select(item => item.TextContent.Trim()));
    }

    [Fact]
    public void Trail_LinksEarlierCrumbsAndDropsTheLast()
    {
        var cut = RenderHeader("Civic", [new Crumb("Vehicles", "/vehicles"), new Crumb("Civic")]);

        var items = cut.FindAll("ol.breadcrumb > li");
        Assert.Equal(["Home", "Garage", "Vehicles"], items.Select(item => item.TextContent.Trim()));
        Assert.Equal("/vehicles", items[2].QuerySelector("a")!.GetAttribute("href"));
        Assert.Empty(cut.FindAll("[aria-current]"));
    }

    [Fact]
    public void Trail_ShowsAnEarlierCrumbWithoutAHrefAsText()
    {
        var cut = RenderHeader("Edit", [new Crumb("Vehicles"), new Crumb("Edit")]);

        var items = cut.FindAll("ol.breadcrumb > li");
        Assert.Equal(["Home", "Garage", "Vehicles"], items.Select(item => item.TextContent.Trim()));
        Assert.Empty(items[2].QuerySelectorAll("a"));
    }

    [Fact]
    public void RendersExactlyOneH1WithTheTitle_BeforeTheBreadcrumb()
    {
        var cut = RenderHeader("My Vehicles", [new Crumb("Vehicles")]);

        var h1 = Assert.Single(cut.FindAll("h1"));
        Assert.Equal("My Vehicles", h1.TextContent.Trim());
        Assert.True(cut.Markup.IndexOf("<h1", StringComparison.Ordinal) < cut.Markup.IndexOf("aria-label=\"Breadcrumb\"", StringComparison.Ordinal));
    }

    [Fact]
    public void IsThePinnedBarAtTheTopOfTheContent()
    {
        var cut = RenderHeader("My Vehicles", [new Crumb("Vehicles")]);

        Assert.Single(cut.FindAll(".mg-pagebar"));
        Assert.NotNull(cut.Find(".mg-pagebar .mg-pagebar-heading h1"));
    }

    [Fact]
    public void Actions_RenderAtTheRightOfTheBar()
    {
        var cut = RenderHeader("My Vehicles", [new Crumb("Vehicles")], "<a class=\"btn btn-primary\" href=\"/vehicles/new\">Add vehicle</a>");

        var bar = cut.Find(".mg-pagebar");
        Assert.Equal("mg-pagebar-actions", bar.Children.Last().ClassName);
        Assert.Equal("Add vehicle", bar.QuerySelector(".mg-pagebar-actions a.btn")!.TextContent);
    }

    [Fact]
    public void NoActions_RendersNoActionsArea()
    {
        var cut = RenderHeader("My Vehicles", [new Crumb("Vehicles")]);

        Assert.Empty(cut.FindAll(".mg-pagebar-actions"));
    }

    [Fact]
    public void CrumbLabels_AreEncoded()
    {
        var cut = RenderHeader("Title", [new Crumb("<script>alert(1)</script>", "/x"), new Crumb("Current")]);

        Assert.DoesNotContain("<script>", cut.Markup);
        Assert.Contains("&lt;script&gt;", cut.Markup);
    }

    [Fact]
    public void CrumbLabels_CanTruncate()
    {
        var cut = RenderHeader("Title", [new Crumb("A very long vehicle nickname", "/x"), new Crumb("Current")]);

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
