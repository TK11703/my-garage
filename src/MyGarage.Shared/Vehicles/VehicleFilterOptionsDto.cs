using MyGarage.Shared.Lookups;

namespace MyGarage.Shared.Vehicles;

public sealed class VehicleFilterOptionsDto
{
    public required IReadOnlyList<string> Makes { get; init; }

    public required IReadOnlyList<string> Colors { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }

    // Lookup items used by the caller's vehicles (hidden ones included).
    public IReadOnlyList<LookupValueDto> VehicleTypes { get; init; } = [];

    public IReadOnlyList<LookupValueDto> FuelTypes { get; init; } = [];

    public IReadOnlyList<LookupValueDto> Transmissions { get; init; } = [];

    public IReadOnlyList<LookupValueDto> Drivetrains { get; init; } = [];
}
