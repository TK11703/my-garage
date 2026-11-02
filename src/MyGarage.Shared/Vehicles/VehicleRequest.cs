namespace MyGarage.Shared.Vehicles;

// Create and update both send the full set of editable fields.
public sealed record VehicleRequest
{
    public string? Nickname { get; init; }

    public string? Vin { get; init; }

    public string? Make { get; init; }

    public string? Model { get; init; }

    public string? Trim { get; init; }

    public int? Year { get; init; }

    public Guid? VehicleTypeId { get; init; }

    public OwnershipType? Ownership { get; init; }

    public DateOnly? PurchasedOn { get; init; }

    public DateOnly? LeaseEndsOn { get; init; }

    public string? Engine { get; init; }

    public int? Horsepower { get; init; }

    public Guid? FuelTypeId { get; init; }

    public Guid? TransmissionId { get; init; }

    public int? Mileage { get; init; }

    public string? Color { get; init; }

    public int? Seats { get; init; }

    public int? Doors { get; init; }

    public Guid? DrivetrainId { get; init; }

    public decimal? WheelSize { get; init; }

    public string? TireSize { get; init; }
}
