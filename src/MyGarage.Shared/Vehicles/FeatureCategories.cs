namespace MyGarage.Shared.Vehicles;

// The fixed set of categories a feature is sorted into, in the order the vehicle page shows them. Other is the last and the fallback.
public static class FeatureCategories
{
    public const string Other = "Other";

    public static IReadOnlyList<string> All { get; } =
    [
        "Safety",
        "Comfort & Convenience",
        "Technology & Audio",
        "Exterior",
        "Interior",
        "Performance & Handling",
        "Wheels & Tires",
        "Towing & Utility",
        "Packages",
        Other,
    ];

    // The matching official name for a category written in any letter case, or null when it is not one of the categories.
    public static string? Normalize(string? value) =>
        value is null ? null : All.FirstOrDefault(category => string.Equals(category, value.Trim(), StringComparison.OrdinalIgnoreCase));

    // Where a feature is shown: its category, or Other while it has none yet.
    public static string Display(string? category) => Normalize(category) ?? Other;
}
