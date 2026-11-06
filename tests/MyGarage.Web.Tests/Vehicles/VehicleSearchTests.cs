using Bunit;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleSearchTests : VehicleTestContext
{
    private IRenderedComponent<VehicleSearchPage> RenderSearch(string uri = "/search")
    {
        NavigateTo(uri);
        return Render<VehicleSearchPage>();
    }

    [Fact]
    public void SearchBox_IsLabeledAndShowsTheCurrentQuery()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderSearch("/search?q=truck");

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

        var cut = RenderSearch();

        cut.WaitForAssertion(() =>
        {
            var card = cut.Find(".mg-search-card");
            Assert.NotNull(card.QuerySelector(".card-body form[role='search'] input#q"));
            Assert.Single(card.QuerySelectorAll("input:not([type='submit'])"));
            Assert.Empty(card.QuerySelectorAll("select"));
        });
    }

    [Fact]
    public void FilterPanel_IsASeparateCardThatSubmitsWithTheSearchForm()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderSearch();

        cut.WaitForAssertion(() => Assert.Contains("Alpha", cut.Markup));
        Assert.Contains("/api/vehicles/filter-options", Requests.Select(request => request.RequestUri!.AbsolutePath));
        var panel = cut.Find(".mg-filter-card");
        Assert.Null(panel.Closest(".mg-search-card"));
        Assert.All(panel.QuerySelectorAll("select, input[type='number']"), field => Assert.Equal("vehicle-search", field.GetAttribute("form")));
        Assert.Equal("vehicle-search", panel.QuerySelector("button[type='submit']")!.GetAttribute("form"));
    }

    [Fact]
    public void ResultTitle_ShowsTheRangeThenTheActiveCriteriaAsInfoBadges()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")], page: 2, totalCount: 30)));

        var cut = RenderSearch("/search?q=Roof&make=Jeep&page=2");

        cut.WaitForAssertion(() =>
        {
            var summary = cut.Find(".mg-results [data-testid='result-summary']");
            Assert.Equal("Showing 13 to 24 of 30 results for", summary.QuerySelector("[data-testid='result-count']")!.TextContent.Trim());
            var badges = summary.QuerySelectorAll("[data-testid='active-criterion']");
            Assert.Equal(["Search: Roof", "Make: Jeep"], badges.Select(badge => badge.TextContent.Replace("\u00D7", string.Empty).Trim()));
            Assert.All(badges, badge => Assert.Contains("bg-info-subtle", badge.ClassList));
            Assert.Empty(cut.FindAll(".mg-filter-card [data-testid='active-criterion']"));
        });
        Assert.Empty(cut.FindAll("[data-testid='showing-range']"));
    }

    [Fact]
    public void ResultTitle_ShowsTheShortLastPage()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha"), Summary("Bravo")], page: 3, totalCount: 26)));

        var cut = RenderSearch("/search?page=3");

        cut.WaitForAssertion(() =>
            Assert.Equal("Showing 25 to 26 of 26 vehicles", cut.Find("[data-testid='result-count']").TextContent.Trim()));
    }

    [Fact]
    public void SinglePage_HasNoFooter()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderSearch("/search?q=alpha");

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='search-hit']")));
        Assert.Empty(cut.FindAll(".mg-results .card-footer"));
    }

    [Fact]
    public void Hits_AreADividedListWithTheSearchTextHighlighted()
    {
        RegisterApi(_ => Json(Page([Summary("Toyota", "Tacoma", mileage: 1200, fuelType: "Gasoline", transmission: "Automatic")])));

        var cut = RenderSearch("/search?q=taco");

        cut.WaitForAssertion(() =>
        {
            var hit = Assert.Single(cut.FindAll("[data-testid='search-hit']"));
            Assert.Equal("Taco", hit.QuerySelector(".mg-hit-title mark")!.TextContent);
            Assert.Equal("2020 Toyota Tacoma", hit.QuerySelector(".mg-hit-title")!.TextContent.Trim());
            Assert.Contains("Gasoline \u00B7 Automatic", hit.QuerySelector(".mg-hit-meta")!.TextContent);
            Assert.Empty(cut.FindAll(".mg-vehicle-card"));
        });
    }

    [Fact]
    public void Hits_ShowWhichFieldMatchedWithContextAroundIt()
    {
        var matches = new[] { new VehicleMatchDto { Field = "Feature", Text = "Heated seats and a very long description about the tow package" } };
        RegisterApi(_ => Json(Page([Summary(matches: matches)])));

        var cut = RenderSearch("/search?q=tow");

        cut.WaitForAssertion(() =>
        {
            var context = cut.Find(".mg-hit-context li");
            Assert.Equal("Feature", context.QuerySelector(".mg-hit-field")!.TextContent);
            Assert.Equal("tow", context.QuerySelector("mark")!.TextContent);
            Assert.Contains("tow package", context.TextContent);
        });
    }

    [Fact]
    public void Hits_SummarizeMatchesBeyondTheFirstThree()
    {
        var matches = Enumerable.Range(1, 5).Select(i => new VehicleMatchDto { Field = "Feature", Text = $"Power item {i}" }).ToList();
        RegisterApi(_ => Json(Page([Summary(matches: matches)])));

        var cut = RenderSearch("/search?q=power");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(3, cut.FindAll(".mg-hit-context li:not([data-testid='more-matches'])").Count);
            Assert.Equal("+ 2 more", cut.Find("[data-testid='more-matches']").TextContent.Trim());
        });
    }

    [Fact]
    public void Breadcrumb_IsJustHome()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderSearch("/search?q=alpha");

        cut.WaitForAssertion(() =>
            Assert.Equal(["Home"], cut.FindAll("nav[aria-label='Breadcrumb'] li").Select(item => item.TextContent.Trim())));
    }

    [Fact]
    public void FilterToggle_IsInThePageBar_NotInTheResultsCard()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderSearch("/search?q=alpha");

        cut.WaitForAssertion(() =>
        {
            var toggle = cut.Find(".mg-pagebar [data-testid='toggle-filters']");
            Assert.Equal("#filter-panel", toggle.GetAttribute("data-bs-target"));
            Assert.Equal("Filter", toggle.TextContent.Trim());
            Assert.Empty(cut.FindAll(".mg-results [data-testid='toggle-filters']"));
            Assert.Empty(cut.FindAll("[data-testid='refine-search']"));
            Assert.Empty(cut.FindAll("[data-testid='back-to-garage']"));
        });
    }

    [Fact]
    public void Search_IsSentToTheApiAndPagingKeepsIt()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")], page: 2, totalCount: 30)));

        var cut = RenderSearch("/search?q=tow%20truck&page=2");

        var query = ListRequests.Single().RequestUri!.Query;
        Assert.Contains("q=tow%20truck", query);
        Assert.Contains("page=2", query);
        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll("nav[aria-label='Pagination'] a").Select(a => a.GetAttribute("href")).ToList();
            Assert.Contains("/search?q=tow%20truck&page=1", links);
            Assert.Contains("/search?q=tow%20truck&page=3", links);
        });
    }

    [Fact]
    public void SearchWithNoResults_ShowsNoMatchStateDistinctFromEmptyGarage()
    {
        RegisterApi(_ => Json(Page([])));

        var cut = RenderSearch("/search?q=zzz");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No vehicles match your search.", cut.Markup);
            Assert.DoesNotContain("You have no vehicles yet", cut.Markup);
            var clear = cut.Find("a[data-testid='no-match-clear']");
            Assert.Equal("/search", clear.GetAttribute("href"));
            Assert.Equal("Clear search", clear.TextContent.Trim());
            Assert.Equal("zzz", cut.Find("input#q").GetAttribute("value"));
        });
    }

    [Fact]
    public void EmptyGarage_WithoutSearch_ShowsTheEmptyStateAndNoSearchCard()
    {
        RegisterApi(_ => Json(Page([])));

        var cut = RenderSearch();

        cut.WaitForAssertion(() => Assert.Contains("You have no vehicles yet", cut.Markup));
        Assert.DoesNotContain("No vehicles match", cut.Markup);
        Assert.Empty(cut.FindAll("form[role='search']"));
    }

    [Fact]
    public void ResultSummary_IsAnnouncedInAPoliteLiveRegion()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha"), Summary("Bravo")], totalCount: 7)));

        var cut = RenderSearch("/search?q=a");

        cut.WaitForAssertion(() =>
        {
            var summary = cut.Find("[data-testid='result-summary']");
            Assert.Equal("polite", summary.GetAttribute("aria-live"));
            Assert.Contains("Showing 1 to 7 of 7 results for", summary.TextContent);
        });
    }

    [Fact]
    public void ApiRejectedInput_ShowsTheApiMessageAndStillShowsTheGarage()
    {
        RegisterApi(request => request.RequestUri!.Query.Contains("q=", StringComparison.Ordinal)
            ? ValidationProblem(("Q", "Search text is too long."))
            : Json(Page([Summary("Alpha")])));

        var cut = RenderSearch("/search?q=toolong");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Search text is too long.", cut.Find("[role='alert']").TextContent);
            Assert.Contains("Alpha", cut.Markup);
        });
    }
}
