using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleListCriteriaStatusTests
{
    private static VehicleListCriteria From(string? status, string? q = null, string? make = null) =>
        VehicleListCriteria.FromQuery(q, make, null, null, null, null, null, status: status);

    [Theory]
    [InlineData(null, "all")]
    [InlineData("active", "active")]
    [InlineData("Archived", "all")]
    [InlineData("closed", "all")]
    [InlineData("", "all")]
    [InlineData("archived", "archived")]
    [InlineData("all", "all")]
    public void FromQuery_KeepsOnlyActiveAndArchived_EverythingElseIsAll(string? status, string expected) =>
        Assert.Equal(expected, From(status).Status);

    [Fact]
    public void ToQueryString_OmitsTheDefaultStatusAndWritesTheOthers()
    {
        Assert.Equal(string.Empty, From("all").ToQueryString());
        Assert.Equal("status=all", From("all").ToQueryString(includeDefaultStatus: true));
        Assert.Equal("status=archived", From("archived").ToQueryString());
        Assert.Equal("status=active&page=2", From("active").ToQueryString(2));
        Assert.Equal("q=truck&make=Ford&status=archived", From("archived", "truck", "Ford").ToQueryString());
        Assert.Equal("page=3", From(null).ToQueryString(3));
    }

    [Fact]
    public void ANonDefaultStatus_IsAnActiveCriterionButNotAFilter()
    {
        var archived = From("archived");

        Assert.True(archived.HasStatus);
        Assert.True(archived.HasAny);
        Assert.False(archived.HasFilters);
        Assert.Equal([("status", "Status: Archived")], archived.Active());
        Assert.Equal([("status", "Status: Active")], From("active").Active());
        Assert.Empty(From("all").Active());
        Assert.False(From("all").HasAny);
    }

    [Fact]
    public void WithoutStatus_ClearsOnlyTheStatus_AndWithoutFiltersKeepsItWithTheSearchText()
    {
        var criteria = From("archived", "truck", "Ford");

        Assert.Equal("all", criteria.Without("status").Status);
        Assert.Equal("truck", criteria.Without("status").Query);
        Assert.Equal("Ford", criteria.Without("status").Make);
        var cleared = criteria.WithoutFilters();
        Assert.Equal("truck", cleared.Query);
        Assert.Equal("archived", cleared.Status);
        Assert.Null(cleared.Make);
    }

    [Fact]
    public void ToHref_CarriesTheStatusToTheSearchPage() => Assert.Equal("/search?status=archived", From("archived").ToHref());
}

public class VehicleStatusFilterPagesTests : VehicleTestContext
{
    private IRenderedComponent<VehicleList> RenderList(string uri = "/vehicles")
    {
        NavigateTo(uri);
        return Render<VehicleList>();
    }

    private IRenderedComponent<VehicleSearchPage> RenderSearch(string uri)
    {
        NavigateTo(uri);
        return Render<VehicleSearchPage>();
    }

    private static string QueryOf(HttpRequestMessage request) => request.RequestUri!.Query;

    private static VehicleSummaryDto ArchivedSummary(string make = "Subaru", string model = "Outback") =>
        new()
        {
            Id = Guid.NewGuid(), Make = make, Model = model, Year = 2019, Mileage = 80_000,
            ArchivedAt = new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero),
        };

    [Fact]
    public void TheListShowsTheThreeStatusLinks_WithAllCurrentByDefault()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderList();

        cut.WaitForAssertion(() =>
        {
            var nav = cut.Find("nav[aria-label='Show vehicles']");
            var links = nav.QuerySelectorAll("a").ToList();
            Assert.Equal(["All", "Active", "Archived"], links.Select(link => link.TextContent.Trim()));
            Assert.Equal(["/vehicles", "/vehicles?status=active", "/vehicles?status=archived"], links.Select(link => link.GetAttribute("href")));
            Assert.Equal("page", links[0].GetAttribute("aria-current"));
            Assert.Contains("active", links[0].ClassList);
            Assert.Null(links[1].GetAttribute("aria-current"));
            Assert.NotNull(cut.Find(".card-header nav[aria-label='Show vehicles']"));
        });
    }

    [Theory]
    [InlineData("/vehicles", "all")]
    [InlineData("/vehicles?status=active", "active")]
    [InlineData("/vehicles?status=archived", "archived")]
    [InlineData("/vehicles?status=all", "all")]
    [InlineData("/vehicles?status=bogus", "all")]
    public void TheListAsksTheApiForTheChosenStatus_AndDoesNotRedirectToSearch(string uri, string expected)
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderList(uri);

        cut.WaitForAssertion(() => Assert.Contains("Alpha", cut.Markup));
        var query = QueryOf(ListRequests.First());
        Assert.Contains($"status={expected}", query);

        Assert.EndsWith(uri, Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
        Assert.Equal(expected, cut.Find("nav[aria-label='Show vehicles'] a[aria-current='page']").GetAttribute("data-status"));
    }

    [Fact]
    public void ThePagerKeepsTheStatus()
    {
        RegisterApi(_ => Json(Page([ArchivedSummary()], page: 1, totalCount: 30)));

        var cut = RenderList("/vehicles?status=archived");

        cut.WaitForAssertion(() =>
        {
            var hrefs = cut.FindAll(".pagination a").Select(link => link.GetAttribute("href")).Where(href => href is not null).ToList();
            Assert.NotEmpty(hrefs);
            Assert.All(hrefs, href => Assert.Contains("status=archived", href));
        });
    }

    [Fact]
    public void ThePagerKeepsTheActiveStatus()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")], page: 1, totalCount: 30)));

        var cut = RenderList("/vehicles?status=active");

        cut.WaitForAssertion(() =>
        {
            var hrefs = cut.FindAll(".pagination a").Select(link => link.GetAttribute("href")).Where(href => href is not null).ToList();
            Assert.NotEmpty(hrefs);
            Assert.All(hrefs, href => Assert.Contains("status=active", href));
        });
    }

    [Fact]
    public void AnArchivedVehicleCard_ShowsAnArchivedBadgeWithTheDate_AnActiveCardShowsNone()
    {
        RegisterApi(_ => Json(Page([ArchivedSummary(), Summary("Ford")])));

        var cut = RenderList("/vehicles?status=all");

        cut.WaitForAssertion(() =>
        {
            var cards = cut.FindAll(".mg-vehicle-card");
            Assert.Equal(2, cards.Count);
            Assert.Equal("Archived Nov 8, 2026", cards[0].QuerySelector("[data-testid='vehicle-archived']")!.TextContent.Trim());
            Assert.Empty(cards[1].QuerySelectorAll("[data-testid='vehicle-archived']"));
        });
    }

    [Fact]
    public void NoVehiclesAtAll_KeepsTheFirstVehicleInvitation()
    {
        RegisterApi(_ => Json(Page([])));

        var cut = RenderList();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("You have no vehicles yet", cut.Markup);
            Assert.Empty(cut.FindAll("nav[aria-label='Show vehicles']"));
        });
    }

    [Fact]
    public void NoActiveVehiclesButSomeArchived_SaysSoAndLinksToTheArchivedChoice()
    {
        RegisterApi(request => Json(QueryOf(request).Contains("status=archived") ? Page([ArchivedSummary()]) : Page([])));

        var cut = RenderList("/vehicles?status=active");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("You have no active vehicles.", cut.Find("[data-testid='no-active']").TextContent);
            Assert.Contains(cut.FindAll("a"), link => link.GetAttribute("href") == "/vehicles?status=archived" && link.TextContent.Contains("Show archived"));
            Assert.NotNull(cut.Find("nav[aria-label='Show vehicles']"));
            Assert.DoesNotContain("You have no vehicles yet", cut.Markup);
        });
    }

    [Fact]
    public void TheArchivedChoiceWithNone_ExplainsWhatArchivingDoes()
    {
        RegisterApi(_ => Json(Page([])));

        var cut = RenderList("/vehicles?status=archived");

        cut.WaitForAssertion(() => Assert.Contains(VehicleMessages.NoArchivedVehicles, cut.Find("[data-testid='no-archived']").TextContent));
    }

    [Fact]
    public void SearchShowsTheStatusLinks_KeepingTheSearchTextAndFilters()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderSearch("/search?q=truck&make=Ford&page=2");

        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll("nav[aria-label='Show vehicles'] a").ToList();
            Assert.Equal(["/search?q=truck&make=Ford", "/search?q=truck&make=Ford&status=active", "/search?q=truck&make=Ford&status=archived"], links.Select(link => link.GetAttribute("href")));
            Assert.Equal("page", links[0].GetAttribute("aria-current"));
        });
    }

    [Fact]
    public void SearchWithAStatus_AsksTheApi_ShowsARemovableBadge_AndKeepsTheStatusInTheForm()
    {
        RegisterApi(_ => Json(Page([ArchivedSummary()])));

        var cut = RenderSearch("/search?q=outback&status=archived");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("status=archived", QueryOf(ListRequests.First()));
            Assert.Contains(Requests, request => request.RequestUri!.AbsolutePath == "/api/vehicles/filter-options" && QueryOf(request).Contains("status=archived"));
            var badges = cut.FindAll("[data-testid='active-criterion']").ToList();
            Assert.Equal(["Search: outback", "Status: Archived"], badges.Select(badge => badge.TextContent.Replace("\u00D7", string.Empty).Trim()));
            Assert.Equal("/search?q=outback", badges[1].QuerySelector("a")!.GetAttribute("href"));
            Assert.Equal("archived", cut.Find("form#vehicle-search input[type='hidden'][name='status']").GetAttribute("value"));
            Assert.Equal("Archived Nov 8, 2026", cut.Find("[data-testid='search-hit'] [data-testid='vehicle-archived']").TextContent.Trim());
        });
    }

    [Fact]
    public void SearchWithTheDefaultStatus_HasNoHiddenStatusField()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));

        var cut = RenderSearch("/search");

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("form#vehicle-search"));
            Assert.Empty(cut.FindAll("input[name='status']"));
        });
    }

    [Fact]
    public void SearchForArchivedVehiclesWithNone_ExplainsWhatArchivingDoes()
    {
        RegisterApi(_ => Json(Page([])));

        var cut = RenderSearch("/search?status=archived");

        cut.WaitForAssertion(() => Assert.Contains(VehicleMessages.NoArchivedVehicles, cut.Find("[data-testid='no-archived']").TextContent));
    }
}
