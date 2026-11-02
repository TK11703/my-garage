namespace MyGarage.Database.Vehicles;

// Optional garage search and filters; every supplied criterion must match.
public sealed record VehicleSearchCriteria
{
    public static VehicleSearchCriteria None { get; } = new();

    // Text matched (as a literal, ignoring case) against nickname, make, model, trim, color, year, VIN, engine, and tag names.
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
}
