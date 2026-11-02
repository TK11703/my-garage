namespace MyGarage.Shared.Lookups;

// The slugs used in /api/lookups/{kind}.
public static class LookupKinds
{
    public const string VehicleTypes = "vehicle-types";

    public const string FuelTypes = "fuel-types";

    public const string Transmissions = "transmissions";

    public const string Drivetrains = "drivetrains";

    public static readonly IReadOnlyList<string> All = [VehicleTypes, FuelTypes, Transmissions, Drivetrains];
}
