namespace MyGarage.Shared.Lookups;

public sealed class LookupsDto
{
    public required IReadOnlyList<LookupItemDto> VehicleTypes { get; init; }

    public required IReadOnlyList<LookupItemDto> FuelTypes { get; init; }

    public required IReadOnlyList<LookupItemDto> Transmissions { get; init; }

    public required IReadOnlyList<LookupItemDto> Drivetrains { get; init; }
}
