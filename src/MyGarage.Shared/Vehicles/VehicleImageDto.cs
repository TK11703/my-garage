namespace MyGarage.Shared.Vehicles;

public sealed class VehicleImageDto
{
    public required Guid Id { get; init; }

    public string? Caption { get; init; }

    public required bool IsMain { get; init; }

    public required string ContentType { get; init; }

    public required int SizeBytes { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
