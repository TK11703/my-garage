using Bunit;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleFiltersTests : VehicleTestContext
{
    private static VehicleFilterOptionsDto Options(string[]? makes = null, string[]? colors = null, string[]? tags = null) =>
        new() { Makes = makes ?? [], Colors = colors ?? [], Tags = tags ?? [] };

    private IRenderedComponent<VehicleList> RenderList(string uri = "/vehicles")
    {
        NavigateTo(uri);
        return Render<VehicleList>();
    }

    [Fact]
    public void SearchBox_IsLabeledAndShowsTheCurrentQuery()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderList("/vehicles?q=truck");

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("label[for='q']"));
            Assert.Equal("truck", cut.Find("input#q").GetAttribute("value"));
            Assert.Equal("get", cut.Find("form[role='search']").GetAttribute("method"));
        });
    }

    [Fact]
    public void FilterPanel_IsClosedWithoutFiltersAndOpenWithThem()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var closed = RenderList("/vehicles?q=alpha");
        closed.WaitForAssertion(() => Assert.False(closed.Find("details").HasAttribute("open")));
    }

    [Fact]
    public void FilterPanel_IsOpenWhenAFilterIsActive()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderList("/vehicles?make=Ford");

        cut.WaitForAssertion(() => Assert.True(cut.Find("details").HasAttribute("open")));
    }

    [Fact]
    public void Selects_AreFilledFromTheCallersGarageOnly()
    {
        FilterOptions = Options(["Ford", "Subaru"], ["Blue", "Red"], ["sunroof", "tow package"]);
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderList();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["", "Ford", "Subaru"], cut.FindAll("select#make option").Select(o => o.GetAttribute("value")));
            Assert.Equal(["", "Blue", "Red"], cut.FindAll("select#color option").Select(o => o.GetAttribute("value")));
            Assert.Equal(["", "sunroof", "tow package"], cut.FindAll("select#tag option").Select(o => o.GetAttribute("value")));
            Assert.Equal(["", "1", "2", "3", "4", "5", "6"], cut.FindAll("select#doors option").Select(o => o.GetAttribute("value")));
        });
    }

    [Fact]
    public void SelectedFilterValues_AreMarkedSelectedEvenWhenNotInTheOptions()
    {
        FilterOptions = Options(["Ford"]);
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderList("/vehicles?make=Ford&color=Teal&doors=4&minMileage=1000&maxMileage=9000");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Ford", cut.Find("select#make option[selected]").GetAttribute("value"));
            Assert.Equal("Teal", cut.Find("select#color option[selected]").GetAttribute("value"));
            Assert.Equal("4", cut.Find("select#doors option[selected]").GetAttribute("value"));
            Assert.Equal("1000", cut.Find("input#minMileage").GetAttribute("value"));
            Assert.Equal("9000", cut.Find("input#maxMileage").GetAttribute("value"));
        });
    }

    [Fact]
    public void Criteria_AreSentToTheApi()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        RenderList("/vehicles?q=tow%20truck&make=Ford&color=Red&doors=4&tag=sunroof&minMileage=1000&maxMileage=9000&page=2");

        var query = ListRequests.Single().RequestUri!.Query;
        Assert.Contains("q=tow%20truck", query);
        Assert.Contains("make=Ford", query);
        Assert.Contains("color=Red", query);
        Assert.Contains("doors=4", query);
        Assert.Contains("tag=sunroof", query);
        Assert.Contains("minMileage=1000", query);
        Assert.Contains("maxMileage=9000", query);
        Assert.Contains("page=2", query);
    }

    [Fact]
    public void ActiveCriteria_AreBadgesThatRemoveJustThatCriterion()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderList("/vehicles?q=ford&make=Ford&minMileage=1000&page=2");

        cut.WaitForAssertion(() =>
        {
            var badges = cut.FindAll("a[data-testid='active-criterion']");
            Assert.Equal(3, badges.Count);
            var make = badges.Single(b => b.TextContent.Contains("Make: Ford"));
            Assert.Equal("/vehicles?q=ford&minMileage=1000", make.GetAttribute("href"));
            Assert.Equal("/vehicles?make=Ford&minMileage=1000", badges.Single(b => b.TextContent.Contains("Search: ford")).GetAttribute("href"));
            Assert.Contains("Min mileage: 1,000", cut.Markup);
            Assert.Equal("/vehicles", cut.Find("a[data-testid='clear-all']").GetAttribute("href"));
        });
    }

    [Fact]
    public void NoCriteria_ShowsNoBadgesAndNoClearAll()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderList();

        cut.WaitForAssertion(() => Assert.Contains("Alpha", cut.Markup));
        Assert.Empty(cut.FindAll("a[data-testid='active-criterion']"));
        Assert.Empty(cut.FindAll("a[data-testid='clear-all']"));
    }

    [Fact]
    public void CriteriaWithNoResults_ShowNoMatchStateDistinctFromEmptyGarage()
    {
        RegisterApi(_ => Json(Page([])));

        var cut = RenderList("/vehicles?q=zzz");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No vehicles match", cut.Markup);
            Assert.DoesNotContain("You have no vehicles yet", cut.Markup);
            Assert.Equal("/vehicles", cut.Find("a[data-testid='no-match-clear']").GetAttribute("href"));
        });
    }

    [Fact]
    public void EmptyGarage_WithoutCriteria_ShowsTheEmptyStateAndNoFilterBar()
    {
        RegisterApi(_ => Json(Page([])));

        var cut = RenderList();

        cut.WaitForAssertion(() => Assert.Contains("You have no vehicles yet", cut.Markup));
        Assert.DoesNotContain("No vehicles match", cut.Markup);
        Assert.Empty(cut.FindAll("form[role='search']"));
    }

    [Fact]
    public void PagerLinks_KeepAllCriteria()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")], page: 2, totalCount: 30)));

        var cut = RenderList("/vehicles?q=ford&make=Ford&page=2");

        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll("nav[aria-label='Pagination'] a").Select(a => a.GetAttribute("href")).ToList();
            Assert.Contains("/vehicles?q=ford&make=Ford&page=1", links);
            Assert.Contains("/vehicles?q=ford&make=Ford&page=3", links);
        });
    }

    [Fact]
    public void ResultSummary_IsAnnouncedInAPoliteLiveRegion()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha"), Summary("Bravo")], totalCount: 7)));

        var cut = RenderList("/vehicles?q=a");

        cut.WaitForAssertion(() =>
        {
            var summary = cut.Find("[data-testid='result-summary']");
            Assert.Equal("polite", summary.GetAttribute("aria-live"));
            Assert.Contains("7 vehicles match", summary.TextContent);
        });
    }

    [Fact]
    public void InvalidCriteria_ShowTheApiMessageAndStillShowTheGarage()
    {
        RegisterApi(request => request.RequestUri!.Query.Contains("minMileage", StringComparison.Ordinal)
            ? ValidationProblem(("MinMileage", "Minimum mileage must not exceed maximum mileage."))
            : Json(Page([Summary("Alpha")])));

        var cut = RenderList("/vehicles?minMileage=5000&maxMileage=1000");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Minimum mileage must not exceed maximum mileage.", cut.Find("[role='alert']").TextContent);
            Assert.Contains("Alpha", cut.Markup);
        });
    }

    [Fact]
    public void NonNumericNumbersInTheUrl_AreIgnored()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        RenderList("/vehicles?doors=abc&minMileage=lots");

        var query = ListRequests.Single().RequestUri!.Query;
        Assert.DoesNotContain("doors", query);
        Assert.DoesNotContain("minMileage", query);
    }
}
