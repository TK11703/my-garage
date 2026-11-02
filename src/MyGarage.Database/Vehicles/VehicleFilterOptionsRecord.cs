using MyGarage.Database.Lookups;

namespace MyGarage.Database.Vehicles;

public sealed record VehicleFilterOptionsRecord(
    IReadOnlyList<string> Makes,
    IReadOnlyList<string> Colors,
    IReadOnlyList<string> Tags)
{
    // Lookup items used by the owner's vehicles, hidden ones included.
    public IReadOnlyList<LookupOptionRecord> VehicleTypes { get; init; } = [];

    public IReadOnlyList<LookupOptionRecord> FuelTypes { get; init; } = [];

    public IReadOnlyList<LookupOptionRecord> Transmissions { get; init; } = [];

    public IReadOnlyList<LookupOptionRecord> Drivetrains { get; init; } = [];
}
