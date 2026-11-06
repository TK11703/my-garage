namespace MyGarage.Database.Dashboard;

// The cost of one kind ("fuel" or "energy") in one calendar month; Month is the first day of that month.
public sealed record FuelEnergySpendRecord
{
    public DateOnly Month { get; init; }

    public string Kind { get; init; } = string.Empty;

    public decimal Total { get; init; }
}
