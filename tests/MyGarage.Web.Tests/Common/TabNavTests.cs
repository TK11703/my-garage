using Bunit;
using MyGarage.Web.Common;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Common;

public class TabNavTests : BunitContext
{
    private static readonly (string Id, string Label, IconName Icon)[] Tabs =
    [
        ("one", "First", IconName.InfoCircle),
        ("two", "Second", IconName.Sliders),
        ("three", "Third", IconName.Image),
    ];

    [Fact]
    public void RendersATablistWithOneIconAndLabelTabPerEntry()
    {
        var cut = Render<TabNav>(parameters => parameters.Add(nav => nav.Tabs, Tabs).Add(nav => nav.Current, "two"));

        var list = cut.Find("ul.nav.mg-tabs");
        Assert.Equal("tablist", list.GetAttribute("role"));
        var buttons = cut.FindAll("button[role='tab']");
        Assert.Equal(["First", "Second", "Third"], buttons.Select(button => button.TextContent.Trim()));
        Assert.All(buttons, button => Assert.Single(button.QuerySelectorAll("svg.bi[aria-hidden='true']")));
        Assert.All(buttons, button => Assert.Equal("tab", button.GetAttribute("data-bs-toggle")));
        Assert.All(buttons, button => Assert.Equal("button", button.GetAttribute("type")));
    }

    [Fact]
    public void OnlyTheCurrentTabIsActiveAndSelected()
    {
        var cut = Render<TabNav>(parameters => parameters.Add(nav => nav.Tabs, Tabs).Add(nav => nav.Current, "two"));

        var buttons = cut.FindAll("button[role='tab']");
        Assert.Equal(["false", "true", "false"], buttons.Select(button => button.GetAttribute("aria-selected")));
        Assert.Equal([false, true, false], buttons.Select(button => button.ClassList.Contains("active")));
    }

    [Fact]
    public void LinksEachTabToItsPaneByIdWithTheDefaultPrefix()
    {
        var cut = Render<TabNav>(parameters => parameters.Add(nav => nav.Tabs, Tabs).Add(nav => nav.Current, "one"));

        var first = cut.Find("button[role='tab']");
        Assert.Equal("tab-one-tab", first.Id);
        Assert.Equal("#tab-one", first.GetAttribute("data-bs-target"));
        Assert.Equal("tab-one", first.GetAttribute("aria-controls"));
    }

    [Fact]
    public void IdPrefixChangesTheGeneratedIds()
    {
        var cut = Render<TabNav>(parameters => parameters
            .Add(nav => nav.Tabs, Tabs).Add(nav => nav.Current, "one").Add(nav => nav.IdPrefix, "lookup"));

        var first = cut.Find("button[role='tab']");
        Assert.Equal("lookup-one-tab", first.Id);
        Assert.Equal("#lookup-one", first.GetAttribute("data-bs-target"));
        Assert.Equal("lookup-one", first.GetAttribute("aria-controls"));
    }

    [Fact]
    public void VehicleTabNav_StillRendersTheFourVehicleTabs()
    {
        var cut = Render<VehicleTabNav>(parameters => parameters.Add(nav => nav.Current, "media"));

        var buttons = cut.FindAll("ul.mg-tabs[role='tablist'] button[role='tab']");
        Assert.Equal(["Basic Info", "Specifications", "Media", "Description"], buttons.Select(button => button.TextContent.Trim()));
        Assert.Equal(["#tab-basic", "#tab-specifications", "#tab-media", "#tab-description"], buttons.Select(button => button.GetAttribute("data-bs-target")));
        Assert.Equal(["false", "false", "true", "false"], buttons.Select(button => button.GetAttribute("aria-selected")));
    }
}
