using System.Net;
using Bunit;
using MyGarage.Shared.Lookups;
using MyGarage.Web.Preferences;
using MyGarage.Web.Tests.Vehicles;

namespace MyGarage.Web.Tests.Preferences;

public class PreferencesPageTests : VehicleTestContext
{
    private IRenderedComponent<MyGarage.Web.Preferences.Preferences> RenderPage()
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        return Render<MyGarage.Web.Preferences.Preferences>();
    }

    [Fact]
    public void ShowsTheTrailAndTitle()
    {
        var cut = RenderPage();

        Assert.Single(cut.FindAll("h1"));
        Assert.Equal("Preferences", cut.Find("h1").TextContent);
        var items = cut.FindAll("nav[aria-label='Breadcrumb'] li");
        Assert.Equal(["Home", "Preferences"], items.Select(item => item.TextContent.Trim()));
        Assert.Equal("page", items[1].GetAttribute("aria-current"));
    }

    [Fact]
    public void AccountDetails_ShowNameAndEmailAsReadOnlyText()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var account = cut.Find("[data-testid='account-details']");
            Assert.Equal(["Name", "Email"], account.QuerySelectorAll("dt").Select(dt => dt.TextContent.Trim()));
            Assert.Equal(["Ada Lovelace", "ada@example.com"], account.QuerySelectorAll("dd").Select(dd => dd.TextContent.Trim()));
            Assert.Empty(account.QuerySelectorAll("input, select, textarea, button, form"));
        });
    }

    [Fact]
    public void AccountDetails_WhenTheUserCannotBeLoaded_SaysNotAvailableAndTheRestStillWorks()
    {
        CurrentUserFailure = HttpStatusCode.ServiceUnavailable;

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["Not available", "Not available"], cut.Find("[data-testid='account-details']").QuerySelectorAll("dd").Select(dd => dd.TextContent.Trim()));
            Assert.Equal(4, cut.FindAll("button[role='tab']").Count);
        });
    }

    [Fact]
    public void AccountDetails_HtmlInTheNameIsEncoded()
    {
        CurrentUser = new() { UserId = "u", DisplayName = "<b>Eve</b>", Email = null };

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='account-details'] b"));
            Assert.Contains("<b>Eve</b>", cut.Find("[data-testid='account-details'] dd").TextContent);
        });
    }

    [Fact]
    public void ShowsTheFourLookupTabsWithTheFirstOpenByDefault()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var tabs = cut.FindAll("ul.mg-tabs[role='tablist'] button[role='tab']");
            Assert.Equal(["Vehicle Types", "Fuel Types", "Transmissions", "Drivetrains"], tabs.Select(tab => tab.TextContent.Trim()));
            Assert.Equal(["true", "false", "false", "false"], tabs.Select(tab => tab.GetAttribute("aria-selected")));
            Assert.Equal(["lookup-vehicle-types"], cut.FindAll(".tab-pane.active").Select(pane => pane.Id));
        });
    }

    [Theory]
    [InlineData("vehicle-types", "lookup-vehicle-types")]
    [InlineData("fuel-types", "lookup-fuel-types")]
    [InlineData("transmissions", "lookup-transmissions")]
    [InlineData("drivetrains", "lookup-drivetrains")]
    [InlineData("bogus", "lookup-vehicle-types")]
    public void TabQuery_SelectsTheOpenTabAndIgnoresUnknownValues(string tab, string expectedPane)
    {
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        NavigateTo($"/preferences?tab={tab}");

        var cut = Render<MyGarage.Web.Preferences.Preferences>();

        cut.WaitForAssertion(() => Assert.Equal([expectedPane], cut.FindAll(".tab-pane.active").Select(pane => pane.Id)));
    }

    [Fact]
    public void EachTabIsACardWithTheNameInTheHeaderItemsInTheBodyAndAddInTheFooter()
    {
        Lookups = new LookupsDto
        {
            VehicleTypes = [],
            FuelTypes = [Item("Gasoline"), Item("Diesel", isHidden: true), Item("Plug-in Hybrid", isDefault: false)],
            Transmissions = [],
            Drivetrains = [],
        };

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var pane = cut.Find("#lookup-fuel-types");
            Assert.Contains("card", pane.ClassList);
            Assert.Equal("Fuel Types", pane.QuerySelector(":scope > .card-header")!.TextContent.Trim());
            Assert.Equal(3, pane.QuerySelectorAll(":scope > .card-body [data-testid='lookup-item']").Length);
            Assert.NotNull(pane.QuerySelector(":scope > .card-footer form[data-testid='add-lookup-fuel-types']"));

            var rows = cut.FindAll("#lookup-fuel-types [data-testid='lookup-item']");
            Assert.Equal(["Gasoline", "Diesel", "Plug-in Hybrid"], rows.Select(row => row.QuerySelector("[data-testid='item-name']")!.TextContent.Trim()));
            Assert.Equal(["Default", "Default", "Custom"], rows.Select(row => row.QuerySelector("[data-testid='item-type']")!.TextContent.Trim()));
            Assert.Equal(["Shown", "Hidden", "Shown"], rows.Select(row => row.QuerySelector("[data-testid='item-status']")!.TextContent.Trim()));
            Assert.Equal([true, false, true], rows.Select(row => row.QuerySelector("input[type='checkbox'][role='switch']")!.HasAttribute("checked")));
            Assert.Equal(["Show Gasoline", "Show Diesel", "Show Plug-in Hybrid"], rows.Select(row => row.QuerySelector("input[role='switch']")!.GetAttribute("aria-label")));
        });
    }

    [Fact]
    public void HiddenItemsAreGreyedOutNotCrossedOut()
    {
        Lookups = new LookupsDto { VehicleTypes = [], FuelTypes = [Item("Gasoline"), Item("Diesel", isHidden: true)], Transmissions = [], Drivetrains = [] };

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("#lookup-fuel-types [data-testid='lookup-item']");
            Assert.DoesNotContain("text-body-secondary", rows[0].ClassList);
            Assert.Contains("text-body-secondary", rows[1].ClassList);
            Assert.Empty(cut.FindAll(".text-decoration-line-through"));
        });
    }

    [Fact]
    public void ListsUseListGroupMarkupNotTables()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("#lookup-vehicle-types .list-group"));
            Assert.Empty(cut.FindAll("table"));
        });
    }

    [Fact]
    public void ItemNamesAreEncoded()
    {
        Lookups = new LookupsDto { VehicleTypes = [Item("<b>Bold</b>", isDefault: false)], FuelTypes = [], Transmissions = [], Drivetrains = [] };

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("#lookup-vehicle-types b"));
            Assert.Equal("<b>Bold</b>", cut.Find("#lookup-vehicle-types [data-testid='item-name']").TextContent.Trim());
        });
    }

    [Fact]
    public void EachTabHasALabeledAddFormLimitedToThirtyCharacters()
    {
        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            foreach (var slug in new[] { "vehicle-types", "fuel-types", "transmissions", "drivetrains" })
            {
                var form = cut.Find($"#lookup-{slug} form[data-testid='add-lookup-{slug}']");
                Assert.NotNull(form.QuerySelector($"label[for='lookup-{slug}-name']"));
                Assert.Equal("30", form.QuerySelector($"input#lookup-{slug}-name")!.GetAttribute("maxlength"));
                Assert.Equal("Add item", form.QuerySelector("button[type='submit']")!.TextContent.Trim());
                Assert.NotNull(form.QuerySelector("button[type='submit'] svg.bi"));
                Assert.False(string.IsNullOrWhiteSpace(form.QuerySelector("button[type='submit']")!.GetAttribute("title")));
            }
        });
    }

    [Fact]
    public void ALookupLoadFailureShowsTheSharedAlertInsteadOfTheTabs()
    {
        LookupsFailure = HttpStatusCode.ServiceUnavailable;

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(MyGarage.Web.Vehicles.VehicleMessages.Unavailable, cut.Markup);
            Assert.Empty(cut.FindAll("button[role='tab']"));
        });
    }

    [Fact]
    public void PreferencesTabs_NormalizeFallsBackToTheFirstTab()
    {
        Assert.Equal("vehicle-types", PreferencesTabs.Normalize(null));
        Assert.Equal("vehicle-types", PreferencesTabs.Normalize("nope"));
        Assert.Equal("drivetrains", PreferencesTabs.Normalize("drivetrains"));
        Assert.Equal(
            [LookupKinds.VehicleTypes, LookupKinds.FuelTypes, LookupKinds.Transmissions, LookupKinds.Drivetrains],
            PreferencesTabs.All.Select(tab => tab.Id));
    }
}
