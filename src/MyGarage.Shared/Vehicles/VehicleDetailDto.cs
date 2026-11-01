namespace MyGarage.Shared.Vehicles;

public sealed class VehicleDetailDto
{
    public required Guid Id { get; init; }

    public required string Nickname { get; init; }

    public required string Make { get; init; }

    public required string Model { get; init; }

    public string? Trim { get; init; }

    public string? Color { get; init; }

    public int? Mileage { get; init; }

    public int? Doors { get; init; }

    public required IReadOnlyList<VehicleTagDto> Tags { get; init; }

    public required IReadOnlyList<VehicleImageDto> Images { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
