using System.Globalization;

namespace MyGarage.Web.Vehicles;

// The garage search text and filters as they appear in the page's query string.
public sealed record VehicleListCriteria
{
    public static VehicleListCriteria None { get; } = new();

    public string? Query { get; init; }

    public string? Make { get; init; }

    public string? Color { get; init; }

    public int? Doors { get; init; }

    public string? Tag { get; init; }

    public int? MinMileage { get; init; }

    public int? MaxMileage { get; init; }

    public bool HasFilters => Make is not null || Color is not null || Doors is not null || Tag is not null || MinMileage is not null || MaxMileage is not null;

    public bool HasAny => Query is not null || HasFilters;

    // Text values are trimmed and blank means absent; numbers that do not parse are ignored.
    public static VehicleListCriteria FromQuery(string? q, string? make, string? color, string? doors, string? tag, string? minMileage, string? maxMileage) =>
        new()
        {
            Query = Clean(q),
            Make = Clean(make),
            Color = Clean(color),
            Doors = ParseNumber(doors),
            Tag = Clean(tag),
            MinMileage = ParseNumber(minMileage),
            MaxMileage = ParseNumber(maxMileage),
        };

    public string ToQueryString(int? page = null)
    {
        var parts = new List<string>();
        Add(parts, "q", Query);
        Add(parts, "make", Make);
        Add(parts, "color", Color);
        Add(parts, "doors", Doors?.ToString(CultureInfo.InvariantCulture));
        Add(parts, "tag", Tag);
        Add(parts, "minMileage", MinMileage?.ToString(CultureInfo.InvariantCulture));
        Add(parts, "maxMileage", MaxMileage?.ToString(CultureInfo.InvariantCulture));
        if (page is not null)
        {
            parts.Add($"page={page.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return string.Join('&', parts);
    }

    // Link to the garage with these criteria and the first page.
    public string ToHref() => ToQueryString() is { Length: > 0 } query ? $"/vehicles?{query}" : "/vehicles";

    // One removable badge per active criterion; Without() gives the criteria with that one cleared.
    public IEnumerable<(string Key, string Label)> Active()
    {
        if (Query is not null)
        {
            yield return ("q", $"Search: {Query}");
        }

        if (Make is not null)
        {
            yield return ("make", $"Make: {Make}");
        }

        if (Color is not null)
        {
            yield return ("color", $"Color: {Color}");
        }

        if (Doors is not null)
        {
            yield return ("doors", $"Doors: {Doors}");
        }

        if (Tag is not null)
        {
            yield return ("tag", $"Tag: {Tag}");
        }

        if (MinMileage is not null)
        {
            yield return ("minMileage", $"Min mileage: {MinMileage.Value.ToString("N0", CultureInfo.InvariantCulture)}");
        }

        if (MaxMileage is not null)
        {
            yield return ("maxMileage", $"Max mileage: {MaxMileage.Value.ToString("N0", CultureInfo.InvariantCulture)}");
        }
    }

    public VehicleListCriteria Without(string key) =>
        key switch
        {
            "q" => this with { Query = null },
            "make" => this with { Make = null },
            "color" => this with { Color = null },
            "doors" => this with { Doors = null },
            "tag" => this with { Tag = null },
            "minMileage" => this with { MinMileage = null },
            "maxMileage" => this with { MaxMileage = null },
            _ => this,
        };

    private static void Add(List<string> parts, string name, string? value)
    {
        if (value is not null)
        {
            parts.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ParseNumber(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
}
