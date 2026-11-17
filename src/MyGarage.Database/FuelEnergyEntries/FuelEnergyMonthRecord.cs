namespace MyGarage.Database.FuelEnergyEntries;

// The cost of one kind ("fuel" or "energy") on one vehicle in one calendar month; Month is the first day of that month.
public sealed record FuelEnergyMonthRecord
{
    public DateOnly Month { get; init; }

    public string Kind { get; init; } = string.Empty;

    public decimal Total { get; init; }
}
