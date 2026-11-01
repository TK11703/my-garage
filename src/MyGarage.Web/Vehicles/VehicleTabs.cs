using MyGarage.Web.Common;

namespace MyGarage.Web.Vehicles;

public static class VehicleTabs
{
    public static readonly (string Id, string Label, IconName Icon)[] All =
    [
        ("basic", "Basic Info", IconName.InfoCircle),
        ("specifications", "Specifications", IconName.Sliders),
        ("media", "Media", IconName.Image),
        ("description", "Description", IconName.FileText),
    ];

    // Anything that is not a known tab means the first one.
    public static string Normalize(string? tab) => All.Any(known => known.Id == tab) ? tab! : "basic";
}
