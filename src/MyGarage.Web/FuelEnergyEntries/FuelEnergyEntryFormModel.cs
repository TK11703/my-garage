using MyGarage.Shared.FuelEnergyEntries;

namespace MyGarage.Web.FuelEnergyEntries;

public sealed class FuelEnergyEntryFormModel
{
    // "fuel" or "energy"; fixed when the form opens and posted back as a hidden value.
    public string KindValue { get; set; } = "fuel";

    public DateOnly? EntryDate { get; set; }

    public decimal? UnitPrice { get; set; }

    public decimal? Quantity { get; set; }

    public decimal? TotalCost { get; set; }

    public FuelEnergyKind Kind => FuelEnergyEntryFormat.ParseKind(KindValue);

    public FuelEnergyEntryRequest ToRequest() =>
        new() { Kind = Kind, EntryDate = EntryDate, UnitPrice = UnitPrice, Quantity = Quantity, TotalCost = TotalCost };

    public static FuelEnergyEntryFormModel For(FuelEnergyKind kind) => new() { KindValue = FuelEnergyEntryFormat.KindValue(kind) };

    public static FuelEnergyEntryFormModel From(FuelEnergyEntryDto entry) =>
        new()
        {
            KindValue = FuelEnergyEntryFormat.KindValue(entry.Kind),
            EntryDate = entry.EntryDate,
            UnitPrice = WithoutTrailingZeros(entry.UnitPrice),
            Quantity = WithoutTrailingZeros(entry.Quantity),
            TotalCost = entry.TotalCost,
        };

    // The database keeps four and three decimals, so 3.459 would otherwise be shown as 3.4590.
    private static decimal WithoutTrailingZeros(decimal value) => value / 1.0000000000000000000000000000m;
}
