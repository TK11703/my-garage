using Bunit;
using MyGarage.Shared.Lookups;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleFilterPanelTests : VehicleTestContext
{
    private static readonly LookupValueDto Truck = new() { Id = Guid.NewGuid(), Name = "Truck" };
    private static readonly LookupValueDto Diesel = new() { Id = Guid.NewGuid(), Name = "Diesel" };

    private IRenderedComponent<VehicleSearchPage> RenderSearch(string uri = "/search")
    {
        FilterOptions = new VehicleFilterOptionsDto
        {
            Makes = ["Ford", "Subaru"],
            Colors = ["Blue", "Red"],
            Tags = ["Sunroof"],
            VehicleTypes = [Truck],
            FuelTypes = [Diesel],
            Transmissions = [new LookupValueDto { Id = Guid.NewGuid(), Name = "Manual" }],
            Drivetrains = [new LookupValueDto { Id = Guid.NewGuid(), Name = "AWD" }],
        };
        RegisterApi(_ => Json(Page([Summary("Alpha")])));
        NavigateTo(uri);
        return Render<VehicleSearchPage>();
    }

    [Fact]
    public void Panel_HasEveryFilterControlLabeledAndBoundToTheSearchForm()
    {
        var cut = RenderSearch();

        cut.WaitForAssertion(() =>
        {
            var panel = cut.Find(".mg-filter-card");
            foreach (var (id, name) in new[]
                     {
                         ("filter-make", "make"), ("filter-color", "color"), ("filter-doors", "doors"), ("filter-tag", "tag"),
                         ("filter-vehicle-type", "vehicleTypeId"), ("filter-fuel-type", "fuelTypeId"),
                         ("filter-transmission", "transmissionId"), ("filter-drivetrain", "drivetrainId"),
                         ("filter-min-mileage", "minMileage"), ("filter-max-mileage", "maxMileage"),
                         ("filter-min-year", "minYear"), ("filter-max-year", "maxYear"),
                     })
            {
                var field = panel.QuerySelector($"#{id}");
                Assert.NotNull(field);
                Assert.Equal(name, field.GetAttribute("name"));
                Assert.Equal("vehicle-search", field.GetAttribute("form"));
                Assert.NotNull(panel.QuerySelector($"label[for='{id}']"));
            }
        });
    }

    [Fact]
    public void Selects_OfferAnyThenTheOptionsFromTheUsersOwnGarage()
    {
        var cut = RenderSearch();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["Any", "Ford", "Subaru"], cut.FindAll("#filter-make option").Select(option => option.TextContent.Trim()));
            Assert.Equal(["Any", "Blue", "Red"], cut.FindAll("#filter-color option").Select(option => option.TextContent.Trim()));
            Assert.Equal(["Any", "Sunroof"], cut.FindAll("#filter-tag option").Select(option => option.TextContent.Trim()));
            Assert.Equal(["Any", "Truck"], cut.FindAll("#filter-vehicle-type option").Select(option => option.TextContent.Trim()));
            Assert.Equal(["Any", "Diesel"], cut.FindAll("#filter-fuel-type option").Select(option => option.TextContent.Trim()));
            Assert.Equal(["Any", "Manual"], cut.FindAll("#filter-transmission option").Select(option => option.TextContent.Trim()));
            Assert.Equal(["Any", "AWD"], cut.FindAll("#filter-drivetrain option").Select(option => option.TextContent.Trim()));
            Assert.Equal(Truck.Id.ToString(), cut.FindAll("#filter-vehicle-type option")[1].GetAttribute("value"));
        });
    }

    [Fact]
    public void Panel_IsCollapsedUntilAFilterIsActive()
    {
        var collapsed = RenderSearch();
        collapsed.WaitForAssertion(() => Assert.Contains("Alpha", collapsed.Markup));

        var body = collapsed.Find(".mg-filter-card .collapse");
        Assert.DoesNotContain("show", body.ClassList);
        Assert.Equal("false", collapsed.Find(".mg-filter-card [data-bs-toggle='collapse']").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Panel_IsOpenWhenAFilterIsActive()
    {
        var cut = RenderSearch("/search?minYear=2000");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("show", cut.Find(".mg-filter-card .collapse").ClassList);
            Assert.Equal("true", cut.Find(".mg-filter-card [data-bs-toggle='collapse']").GetAttribute("aria-expanded"));
            Assert.Equal("2000", cut.Find("#filter-min-year").GetAttribute("value"));
        });
    }

    [Fact]
    public void ActiveCriteria_AreRemovableBadgesWithAClearAllLink()
    {
        var cut = RenderSearch($"/search?q=tow&minYear=2000&fuelTypeId={Diesel.Id}");

        cut.WaitForAssertion(() =>
        {
            var badges = cut.FindAll("[data-testid='active-criterion']");
            Assert.Equal(["Search: tow", "Min year: 2000", "Fuel type: Diesel"], badges.Select(badge => badge.TextContent.Trim().Replace("\u00D7", string.Empty).Trim()));
            var removeMinYear = badges[1].QuerySelector("a")!;
            Assert.Equal($"/search?q=tow&fuelTypeId={Diesel.Id}", removeMinYear.GetAttribute("href"));
            Assert.Equal("Remove filter Min year: 2000", removeMinYear.GetAttribute("aria-label"));
            Assert.Equal("/search?q=tow", cut.Find("a[data-testid='clear-all']").GetAttribute("href"));
        });
    }

    [Fact]
    public void Panel_IsAnAccordionItemWithAnIconAndClearAllBesideApply()
    {
        var cut = RenderSearch("/search?minYear=2000");

        cut.WaitForAssertion(() =>
        {
            var item = cut.Find(".mg-filter-card.accordion > .accordion-item");
            var header = item.QuerySelector(".accordion-header button.accordion-button")!;
            Assert.Equal("Filters", header.TextContent.Trim());
            Assert.Equal("true", header.QuerySelector("svg.bi")!.GetAttribute("aria-hidden"));
            Assert.Equal("filter-panel", header.GetAttribute("aria-controls"));
            Assert.Contains("accordion-collapse", item.QuerySelector("#filter-panel")!.ClassList);

            var actions = item.QuerySelector("#filter-panel button[type='submit']")!.ParentElement!;
            Assert.Equal(["Apply", "Clear all"], actions.Children.Select(child => child.TextContent.Trim()));
            Assert.Empty(item.QuerySelectorAll(".accordion-header [data-testid='clear-all']"));
        });
    }

    [Fact]
    public void ClearAll_IsOnlyOfferedForFilters_NotForASearchOnly()
    {
        var cut = RenderSearch("/search?q=tow");

        cut.WaitForAssertion(() => Assert.Contains("Alpha", cut.Markup));
        Assert.Empty(cut.FindAll("a[data-testid='clear-all']"));
    }

    [Fact]
    public void NoCriteria_ShowsNoBadgesOrClearAll()
    {
        var cut = RenderSearch();

        cut.WaitForAssertion(() => Assert.Contains("Alpha", cut.Markup));
        Assert.Empty(cut.FindAll("[data-testid='active-criterion']"));
        Assert.Empty(cut.FindAll("a[data-testid='clear-all']"));
    }

    [Fact]
    public void NewFiltersAreSentToTheApiAndPagingKeepsThem()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")], page: 2, totalCount: 30)));
        NavigateTo($"/search?minYear=2000&maxYear=2020&vehicleTypeId={Truck.Id}&page=2");

        var cut = Render<VehicleSearchPage>();

        var query = ListRequests.Single().RequestUri!.Query;
        Assert.Contains("minYear=2000", query);
        Assert.Contains("maxYear=2020", query);
        Assert.Contains($"vehicleTypeId={Truck.Id}", query);
        cut.WaitForAssertion(() =>
        {
            var links = cut.FindAll("nav[aria-label='Pagination'] a").Select(a => a.GetAttribute("href")).ToList();
            Assert.Contains($"/search?minYear=2000&maxYear=2020&vehicleTypeId={Truck.Id}&page=1", links);
        });
    }

    [Fact]
    public void MinimumYearAboveMaximum_ShowsTheApiMessageAndTheGarageStaysUsable()
    {
        RegisterApi(request => request.RequestUri!.Query.Contains("minYear=", StringComparison.Ordinal)
            ? ValidationProblem(("MinYear", "Minimum year must not exceed maximum year."))
            : Json(Page([Summary("Alpha")])));
        NavigateTo("/search?minYear=2020&maxYear=2000");

        var cut = Render<VehicleSearchPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Minimum year must not exceed maximum year.", cut.Find("[role='alert']").TextContent);
            Assert.Contains("Alpha", cut.Markup);
        });
    }

    [Fact]
    public void FilteredNoMatch_IsDistinctFromTheEmptyGarage()
    {
        RegisterApi(_ => Json(Page([])));
        NavigateTo("/search?minYear=2030");

        var cut = Render<VehicleSearchPage>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("No vehicles match your search.", cut.Markup);
            Assert.DoesNotContain("You have no vehicles yet", cut.Markup);
        });
    }

    [Fact]
    public void WhenFilterOptionsCannotBeLoaded_TheGarageStillShowsAndTheNumberFiltersRemain()
    {
        RegisterApi(_ => Json(Page([Summary("Alpha")])));
        FilterOptionsFailure = System.Net.HttpStatusCode.ServiceUnavailable;
        var cut = Render<VehicleSearchPage>();

        cut.WaitForAssertion(() => Assert.Contains("Alpha", cut.Markup));
        Assert.NotNull(cut.Find("#filter-min-year"));
        Assert.Empty(cut.FindAll("#filter-vehicle-type"));
    }

    [Fact]
    public void SearchBoxPlaceholderMentionsTheNewSearchableFields()
    {
        var cut = RenderSearch();

        cut.WaitForAssertion(() =>
        {
            var placeholder = cut.Find("input#q").GetAttribute("placeholder")!;
            Assert.Contains("year", placeholder, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("VIN", placeholder);
            Assert.Contains("engine", placeholder, StringComparison.OrdinalIgnoreCase);
        });
    }
}
