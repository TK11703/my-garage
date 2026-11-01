using Bunit;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleSearchTests : VehicleTestContext
{
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
    public void SearchCard_HoldsOnlyTheSearchField()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderList();

        cut.WaitForAssertion(() =>
        {
            var card = cut.Find(".mg-search-card");
            Assert.NotNull(card.QuerySelector(".card-body form[role='search'] input#q"));
            Assert.Single(card.QuerySelectorAll("input:not([type='submit'])"));
            Assert.Empty(card.QuerySelectorAll("select"));
        });
    }

    [Fact]
    public void ThePageOffersNoFilterControls()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderList();

        cut.WaitForAssertion(() => Assert.Contains("Alpha", cut.Markup));
        Assert.Empty(cut.FindAll("select"));
        Assert.Empty(cut.FindAll("input[type='number']"));
        Assert.Empty(cut.FindAll("[data-bs-toggle='collapse']"));
        Assert.DoesNotContain("Filters", cut.Markup);
        Assert.DoesNotContain("/api/vehicles/filter-options", Requests.Select(request => request.RequestUri!.AbsolutePath));
    }

    [Fact]
    public void Footer_ShowsTheRangeOnThisPage()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")], page: 2, totalCount: 30)));

        var cut = RenderList("/vehicles?page=2");

        cut.WaitForAssertion(() =>
            Assert.Equal("Showing 13 to 24 of 30 entries", cut.Find("[data-testid='showing-range']").TextContent.Trim()));
    }

    [Fact]
    public void Footer_ShowsTheShortLastPage()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha"), Summary("Bravo")], page: 3, totalCount: 26)));

        var cut = RenderList("/vehicles?page=3");

        cut.WaitForAssertion(() =>
            Assert.Equal("Showing 25 to 26 of 26 entries", cut.Find("[data-testid='showing-range']").TextContent.Trim()));
    }

    [Fact]
    public void Vehicles_AreShownInUpToFourColumns()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderList();

        cut.WaitForAssertion(() =>
        {
            var grid = cut.Find(".mg-results .row.row-cols-1");
            Assert.Contains("row-cols-xl-4", grid.ClassList);
            Assert.Contains("row-cols-lg-3", grid.ClassList);
        });
    }

    [Fact]
    public void Search_IsSentToTheApiAndPagingKeepsIt()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")], page: 2, totalCount: 30)));

        var cut = RenderList("/vehicles?q=tow%20truck&page=2");

        var query = ListRequests.Single().RequestUri!.Query;
        Assert.Contains("q=tow%20truck", query);
        Assert.Contains("page=2", query);
        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll("nav[aria-label='Pagination'] a").Select(a => a.GetAttribute("href")).ToList();
            Assert.Contains("/vehicles?q=tow%20truck&page=1", links);
            Assert.Contains("/vehicles?q=tow%20truck&page=3", links);
        });
    }

    [Fact]
    public void SearchWithNoResults_ShowsNoMatchStateDistinctFromEmptyGarage()
    {
        RegisterApi(_ => Json(Page([])));

        var cut = RenderList("/vehicles?q=zzz");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No vehicles match your search.", cut.Markup);
            Assert.DoesNotContain("You have no vehicles yet", cut.Markup);
            var clear = cut.Find("a[data-testid='no-match-clear']");
            Assert.Equal("/vehicles", clear.GetAttribute("href"));
            Assert.Equal("Clear search", clear.TextContent.Trim());
            Assert.Equal("zzz", cut.Find("input#q").GetAttribute("value"));
        });
    }

    [Fact]
    public void EmptyGarage_WithoutSearch_ShowsTheEmptyStateAndNoSearchCard()
    {
        RegisterApi(_ => Json(Page([])));

        var cut = RenderList();

        cut.WaitForAssertion(() => Assert.Contains("You have no vehicles yet", cut.Markup));
        Assert.DoesNotContain("No vehicles match", cut.Markup);
        Assert.Empty(cut.FindAll("form[role='search']"));
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
            Assert.Contains("7 vehicles found", summary.TextContent);
        });
    }

    [Fact]
    public void ApiRejectedInput_ShowsTheApiMessageAndStillShowsTheGarage()
    {
        RegisterApi(request => request.RequestUri!.Query.Contains("q=", StringComparison.Ordinal)
            ? ValidationProblem(("Q", "Search text is too long."))
            : Json(Page([Summary("Alpha")])));

        var cut = RenderList("/vehicles?q=toolong");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Search text is too long.", cut.Find("[role='alert']").TextContent);
            Assert.Contains("Alpha", cut.Markup);
        });
    }
}
