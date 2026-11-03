namespace MyGarage.Shared.FuelEnergyEntries;

public sealed class FuelEnergyEntryRequest
{
    public FuelEnergyKind? Kind { get; init; }

    public DateOnly? EntryDate { get; init; }

    // Price per gallon or per kWh.
    public decimal? UnitPrice { get; init; }

    // Total gallons or kWh delivered.
    public decimal? Quantity { get; init; }

    // Left empty, the total is calculated from the price and the amount.
    public decimal? TotalCost { get; init; }
}
