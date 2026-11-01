namespace MyGarage.Database.Vehicles;

public sealed record VehicleFilterOptionsRecord(
    IReadOnlyList<string> Makes,
    IReadOnlyList<string> Colors,
    IReadOnlyList<string> Tags);
