namespace MyGarage.Shared.Vehicles;

public sealed class VehicleSummaryDto
{
    public required Guid Id { get; init; }

    public required string Nickname { get; init; }

    // Null for vehicles saved before the year was collected.
    public int? Year { get; init; }

    public required string Make { get; init; }

    public required string Model { get; init; }

    public int? Mileage { get; init; }

    public Guid? MainImageId { get; init; }
}
