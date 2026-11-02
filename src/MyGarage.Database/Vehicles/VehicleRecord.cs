namespace MyGarage.Database.Vehicles;

public sealed record VehicleRecord
{
    public Guid Id { get; init; }

    public string OwnerId { get; init; } = string.Empty;

    public string? Vin { get; init; }

    public string Make { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string? Trim { get; init; }

    public int? Year { get; init; }

    public Guid? VehicleTypeId { get; init; }

    // Read-only: filled by GetById from the referenced lookup item.
    public string? VehicleTypeName { get; init; }

    // purchased or leased.
    public string? Ownership { get; init; }

    public DateOnly? PurchasedOn { get; init; }

    public DateOnly? LeaseEndsOn { get; init; }

    public string? Engine { get; init; }

    public int? Horsepower { get; init; }

    public Guid? FuelTypeId { get; init; }

    public string? FuelTypeName { get; init; }

    public Guid? TransmissionId { get; init; }

    public string? TransmissionName { get; init; }

    public int? Mileage { get; init; }

    public string? Color { get; init; }

    public int? Seats { get; init; }

    public int? Doors { get; init; }

    public Guid? DrivetrainId { get; init; }

    public string? DrivetrainName { get; init; }

    public decimal? WheelSize { get; init; }

    public string? TireSize { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public string UpdatedBy { get; init; } = string.Empty;
}
