using MyGarage.Web.Common;

namespace MyGarage.Web.Components.Layout;

internal sealed record NavItem(string Label, string Href, IconName Icon, bool Exact = false, params string[] Excluding);

internal sealed record NavGroup(string Id, string Label, NavItem[] Items);

// The sidebar's groups and items; the page bar reads the same groups so its breadcrumb names the menu group.
internal static class NavMenu
{
    // Shown above the groups, with no heading.
    public static readonly NavItem Home = new("Home", "/", IconName.HouseDoor, Exact: true);

    public static readonly NavGroup[] Groups =
    [
        new("garage", "Garage",
        [
            new NavItem("My Vehicles", "/vehicles", IconName.CarFront, false, "/vehicles/new"),
            new NavItem("Add Vehicle", "/vehicles/new", IconName.PlusCircle, Exact: true),
        ]),
        new("maintenance", "Maintenance",
        [
            new NavItem("Maintenance Drafts", "/drafts", IconName.Inbox, false, "/drafts/upload"),
            new NavItem("Add Maintenance", "/drafts/upload", IconName.CloudUpload, Exact: true),
        ]),
        new("help", "Help",
        [
            new NavItem("FAQ", "/faq", IconName.QuestionCircle, Exact: true),
            new NavItem("About", "/about", IconName.InfoCircle, Exact: true),
        ]),
    ];

    // Which group a page sits under, from the first crumb of its trail; null for pages outside the menu.
    private static readonly Dictionary<string, string> GroupByFirstCrumb = new(StringComparer.Ordinal)
    {
        ["Vehicles"] = "Garage",
        ["Maintenance Drafts"] = "Maintenance",
        ["FAQ"] = "Help",
        ["About"] = "Help",
    };

    public static string? GroupOf(string firstCrumb) => GroupByFirstCrumb.GetValueOrDefault(firstCrumb);
}
