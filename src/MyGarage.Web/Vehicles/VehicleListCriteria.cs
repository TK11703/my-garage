using System.Globalization;
using MyGarage.Shared.Lookups;
using MyGarage.Shared.Vehicles;

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

    public int? MinYear { get; init; }

    public int? MaxYear { get; init; }

    public Guid? VehicleTypeId { get; init; }

    public Guid? FuelTypeId { get; init; }

    public Guid? TransmissionId { get; init; }

    public Guid? DrivetrainId { get; init; }

    // all (the default), active, or archived; anything else in a link means all.
    public string Status { get; init; } = VehicleStatusFilter.All;

    public bool HasStatus => Status != VehicleStatusFilter.All;

    public bool HasFilters =>
        Make is not null || Color is not null || Doors is not null || Tag is not null || MinMileage is not null || MaxMileage is not null
        || MinYear is not null || MaxYear is not null || VehicleTypeId is not null || FuelTypeId is not null
        || TransmissionId is not null || DrivetrainId is not null;

    public bool HasAny => Query is not null || HasFilters || HasStatus;

    // Text values are trimmed and blank means absent; numbers and ids that do not parse are ignored.
    public static VehicleListCriteria FromQuery(
        string? q,
        string? make,
        string? color,
        string? doors,
        string? tag,
        string? minMileage,
        string? maxMileage,
        string? minYear = null,
        string? maxYear = null,
        string? vehicleTypeId = null,
        string? fuelTypeId = null,
        string? transmissionId = null,
        string? drivetrainId = null,
        string? status = null) =>
        new()
        {
            Query = Clean(q),
            Make = Clean(make),
            Color = Clean(color),
            Doors = ParseNumber(doors),
            Tag = Clean(tag),
            MinMileage = ParseNumber(minMileage),
            MaxMileage = ParseNumber(maxMileage),
            MinYear = ParseNumber(minYear),
            MaxYear = ParseNumber(maxYear),
            VehicleTypeId = ParseGuid(vehicleTypeId),
            FuelTypeId = ParseGuid(fuelTypeId),
            TransmissionId = ParseGuid(transmissionId),
            DrivetrainId = ParseGuid(drivetrainId),
            Status = status is VehicleStatusFilter.Archived or VehicleStatusFilter.Active ? status : VehicleStatusFilter.All,
        };

    // The API treats a missing status as active, so requests to it always name the status.
    public string ToQueryString(int? page = null, bool includeDefaultStatus = false)
    {
        var parts = new List<string>();
        Add(parts, "q", Query);
        Add(parts, "make", Make);
        Add(parts, "color", Color);
        Add(parts, "doors", Doors?.ToString(CultureInfo.InvariantCulture));
        Add(parts, "tag", Tag);
        Add(parts, "minMileage", MinMileage?.ToString(CultureInfo.InvariantCulture));
        Add(parts, "maxMileage", MaxMileage?.ToString(CultureInfo.InvariantCulture));
        Add(parts, "minYear", MinYear?.ToString(CultureInfo.InvariantCulture));
        Add(parts, "maxYear", MaxYear?.ToString(CultureInfo.InvariantCulture));
        Add(parts, "vehicleTypeId", VehicleTypeId?.ToString());
        Add(parts, "fuelTypeId", FuelTypeId?.ToString());
        Add(parts, "transmissionId", TransmissionId?.ToString());
        Add(parts, "drivetrainId", DrivetrainId?.ToString());
        Add(parts, "status", HasStatus || includeDefaultStatus ? Status : null);
        if (page is not null)
        {
            parts.Add($"page={page.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return string.Join('&', parts);
    }

    // Link to the search page with these criteria and the first page.
    public string ToHref() => ToQueryString() is { Length: > 0 } query ? $"/search?{query}" : "/search";

    // One removable badge per active criterion; Without() gives the criteria with that one cleared.
    // Lookup choices are named from the user's own filter options and never shown as identifiers.
    public IEnumerable<(string Key, string Label)> Active(VehicleFilterOptionsDto? options = null)
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

        if (MinYear is not null)
        {
            yield return ("minYear", $"Min year: {MinYear.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (MaxYear is not null)
        {
            yield return ("maxYear", $"Max year: {MaxYear.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (VehicleTypeId is { } vehicleType)
        {
            yield return ("vehicleTypeId", $"Vehicle type: {NameOf(options?.VehicleTypes, vehicleType)}");
        }

        if (FuelTypeId is { } fuelType)
        {
            yield return ("fuelTypeId", $"Fuel type: {NameOf(options?.FuelTypes, fuelType)}");
        }

        if (TransmissionId is { } transmission)
        {
            yield return ("transmissionId", $"Transmission: {NameOf(options?.Transmissions, transmission)}");
        }

        if (DrivetrainId is { } drivetrain)
        {
            yield return ("drivetrainId", $"Drivetrain: {NameOf(options?.Drivetrains, drivetrain)}");
        }

        if (HasStatus)
        {
            yield return ("status", Status == VehicleStatusFilter.Archived ? "Status: Archived" : "Status: Active");
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
            "minYear" => this with { MinYear = null },
            "maxYear" => this with { MaxYear = null },
            "vehicleTypeId" => this with { VehicleTypeId = null },
            "fuelTypeId" => this with { FuelTypeId = null },
            "transmissionId" => this with { TransmissionId = null },
            "drivetrainId" => this with { DrivetrainId = null },
            "status" => this with { Status = VehicleStatusFilter.All },
            _ => this,
        };

    // Only the search text and the status survive; every filter is cleared.
    public VehicleListCriteria WithoutFilters() => None with { Query = Query, Status = Status };

    private static string NameOf(IReadOnlyList<LookupValueDto>? choices, Guid id) =>
        choices?.FirstOrDefault(choice => choice.Id == id)?.Name ?? "selected";

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

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
