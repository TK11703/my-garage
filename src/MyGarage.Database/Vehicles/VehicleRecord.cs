namespace MyGarage.Database.Vehicles;

public sealed record VehicleRecord
{
    public Guid Id { get; init; }

    public string OwnerId { get; init; } = string.Empty;

    public string Nickname { get; init; } = string.Empty;

    public string Make { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string? Trim { get; init; }

    public string? Color { get; init; }

    public int? Mileage { get; init; }

    public int? Doors { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public string UpdatedBy { get; init; } = string.Empty;
}
