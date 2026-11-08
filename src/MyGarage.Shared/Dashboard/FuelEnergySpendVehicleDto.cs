namespace MyGarage.Shared.Dashboard;

// One vehicle's share of a month's fuel and charging cost.
public sealed class FuelEnergySpendVehicleDto
{
    public required Guid VehicleId { get; init; }

    public required string VehicleName { get; init; }

    public required decimal Fuel { get; init; }

    public required decimal Charging { get; init; }
}
