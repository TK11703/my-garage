namespace MyGarage.Shared.Dashboard;

// What the owner spent in one calendar month, in total and per vehicle; Month is the first day of the month.
public sealed class FuelEnergySpendMonthDto
{
    public required DateOnly Month { get; init; }

    public required decimal Fuel { get; init; }

    public required decimal Charging { get; init; }

    // Only vehicles with a cost that month, in no particular order.
    public IReadOnlyList<FuelEnergySpendVehicleDto> Vehicles { get; init; } = [];
}
