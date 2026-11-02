using MyGarage.Web.Common;

namespace MyGarage.Web.Vehicles;

public static class VehicleTabs
{
    public static readonly (string Id, string Label, IconName Icon)[] All =
    [
        ("details", "Details", IconName.InfoCircle),
        ("media", "Media", IconName.Image),
        ("description", "Description", IconName.FileText),
        ("service-records", "Maintenance", IconName.Wrench),
    ];

    // Anything that is not a known tab means the first one.
    public static string Normalize(string? tab) => All.Any(known => known.Id == tab) ? tab! : "details";

    // The edit form has no service records pane; they are managed on their own pages.
    public static readonly (string Id, string Label, IconName Icon)[] Edit = [.. All.Where(tab => tab.Id != ServiceRecords)];

    public const string ServiceRecords = "service-records";

    public static string NormalizeForEdit(string? tab) => Edit.Any(known => known.Id == tab) ? tab! : "details";
}
