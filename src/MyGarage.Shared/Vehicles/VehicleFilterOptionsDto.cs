namespace MyGarage.Shared.Vehicles;

public sealed class VehicleFilterOptionsDto
{
    public required IReadOnlyList<string> Makes { get; init; }

    public required IReadOnlyList<string> Colors { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }
}
