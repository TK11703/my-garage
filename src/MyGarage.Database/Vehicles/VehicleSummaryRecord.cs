namespace MyGarage.Database.Vehicles;

public sealed record VehicleSummaryRecord
{
    public Guid Id { get; init; }

    public string Nickname { get; init; } = string.Empty;

    public string Make { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public int? Mileage { get; init; }

    public Guid? MainImageId { get; init; }
}
