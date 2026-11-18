namespace MyGarage.Web.Components.Layout;

// One rail item: the icon is what is drawn; the label is the accessible name and the tooltip.
// AlsoExact lists extra paths that count as this item (the home page belongs to the garage).
internal sealed record RailItem(string Label, MyGarage.Web.Common.IconName Icon, string Href, bool Exact = false, string[]? Excluding = null, string[]? AlsoExact = null);

// The rail's items; the page header reads the same names so its location line says where a page sits.
internal static class NavMenu
{
    public static readonly RailItem Garage = new("Garage", MyGarage.Web.Common.IconName.CarFront, "/vehicles", Excluding: ["/vehicles/new"], AlsoExact: ["/"]);

    public static readonly RailItem AddVehicle = new("Add vehicle", MyGarage.Web.Common.IconName.PlusCircle, "/vehicles/new", Exact: true);

    public static readonly RailItem Reminders = new("Reminders", MyGarage.Web.Common.IconName.CalendarCheck, "/reminders", Exact: true);

    public static readonly RailItem Drafts = new("Drafts", MyGarage.Web.Common.IconName.Inbox, "/drafts");

    public static readonly RailItem Search = new("Search", MyGarage.Web.Common.IconName.Search, "/search", Exact: true);

    public static readonly RailItem Dashboard = new("Dashboard", MyGarage.Web.Common.IconName.Speedometer, "/dashboard", Exact: true);

    public static readonly RailItem[] Items = [Garage, AddVehicle, Reminders, Drafts, Search, Dashboard];

    // Which area a page sits under, from the first crumb of its trail; null for pages outside the rail.
    private static readonly Dictionary<string, string> GroupByFirstCrumb = new(StringComparer.Ordinal)
    {
        ["Vehicles"] = "Garage",
        ["Reminders"] = "Garage",
        ["Maintenance Drafts"] = "Maintenance",
        ["FAQ"] = "Help",
        ["About"] = "Help",
    };

    public static string? GroupOf(string firstCrumb) => GroupByFirstCrumb.GetValueOrDefault(firstCrumb);

    public static bool IsActive(RailItem item, string path)
    {
        if (item.AlsoExact?.Any(also => path.Equals(also, StringComparison.OrdinalIgnoreCase)) == true)
        {
            return true;
        }

        if (item.Exact)
        {
            return path.Equals(item.Href, StringComparison.OrdinalIgnoreCase);
        }

        return Under(path, item.Href) && !(item.Excluding?.Any(excluded => Under(path, excluded)) ?? false);
    }

    private static bool Under(string path, string href) =>
        path.Equals(href, StringComparison.OrdinalIgnoreCase) || path.StartsWith(href + "/", StringComparison.OrdinalIgnoreCase);
}
