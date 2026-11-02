using MyGarage.Shared.Lookups;
using MyGarage.Web.Common;

namespace MyGarage.Web.Preferences;

public static class PreferencesTabs
{
    // One tab per lookup; the id is the lookup's route slug.
    public static readonly (string Id, string Label, IconName Icon)[] All =
    [
        (LookupKinds.VehicleTypes, "Vehicle Types", IconName.Image),
        (LookupKinds.FuelTypes, "Fuel Types", IconName.Speedometer),
        (LookupKinds.Transmissions, "Transmissions", IconName.Sliders),
        (LookupKinds.Drivetrains, "Drivetrains", IconName.InfoCircle),
    ];

    // Anything that is not a known tab means the first one.
    public static string Normalize(string? tab) => All.Any(known => known.Id == tab) ? tab! : All[0].Id;
}
