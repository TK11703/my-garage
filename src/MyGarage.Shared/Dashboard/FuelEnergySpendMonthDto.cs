namespace MyGarage.Shared.Dashboard;

// What the owner spent in one calendar month across all their vehicles; Month is the first day of the month.
public sealed class FuelEnergySpendMonthDto
{
    public required DateOnly Month { get; init; }

    public required decimal Fuel { get; init; }

    public required decimal Charging { get; init; }
}
