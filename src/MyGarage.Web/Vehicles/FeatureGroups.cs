using MyGarage.Shared.Vehicles;

namespace MyGarage.Web.Vehicles;

// One category card on the vehicle page: its features, ordered by name.
public sealed record FeatureGroup(string Category, IReadOnlyList<VehicleTagDto> Features);

public static class FeatureGroups
{
    // How many features a category card lists before the rest are folded away.
    public const int PreviewCount = 7;

    public const int MaxQueryLength = 30;

    // The search text as it is used: trimmed and no longer than a feature name.
    public static string Clean(string? query)
    {
        var text = query?.Trim() ?? string.Empty;
        return text.Length > MaxQueryLength ? text[..MaxQueryLength].TrimEnd() : text;
    }

    // Groups in the fixed category order, leaving out categories with no matching feature.
    public static IReadOnlyList<FeatureGroup> Build(IEnumerable<VehicleTagDto> tags, string query)
    {
        var matching = query.Length == 0
            ? tags.ToList()
            : tags.Where(tag => tag.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        return
        [
            .. FeatureCategories.All
                .Select(category => new FeatureGroup(
                    category,
                    [.. matching.Where(tag => FeatureCategories.Display(tag.Category) == category).OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)]))
                .Where(group => group.Features.Count > 0),
        ];
    }
}
