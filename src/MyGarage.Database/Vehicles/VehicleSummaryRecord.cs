namespace MyGarage.Database.Vehicles;

public sealed record VehicleSummaryRecord
{
    public Guid Id { get; init; }

    public int? Year { get; init; }

    public string Make { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public int? Mileage { get; init; }

    public Guid? MainImageId { get; init; }

    public string? FuelTypeName { get; init; }

    public string? TransmissionName { get; init; }

    // Stored text: "purchased" or "leased".
    public string? Ownership { get; init; }

    public DateOnly? PurchasedOn { get; init; }
}
