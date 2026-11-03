using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Web.Common;

namespace MyGarage.Web.FuelEnergyEntries;

public sealed record FuelEnergyLabels(string Date, string Price, string Quantity, string Total, string TotalHelp, string Unit);

public static class FuelEnergyEntryFormat
{
    public static string KindLabel(FuelEnergyKind kind) => kind == FuelEnergyKind.Fuel ? "Fuel" : "Charge";

    // Lower case, for sentences such as "Delete this fuel entry".
    public static string Noun(FuelEnergyKind kind) => kind == FuelEnergyKind.Fuel ? "fuel entry" : "charging entry";

    // "12.345 gal at $3.459/gal" or "28.5 kWh at $0.1342/kWh".
    public static string Detail(FuelEnergyEntryDto entry)
    {
        var unit = entry.Kind == FuelEnergyKind.Fuel ? "gal" : "kWh";
        return $"{DisplayFormat.Quantity(entry.Quantity)} {unit} at {DisplayFormat.UnitPrice(entry.UnitPrice)}/{unit}";
    }

    public static string EditName(FuelEnergyEntryDto entry) => $"Edit {KindLabel(entry.Kind).ToLowerInvariant()} from {DisplayFormat.Date(entry.EntryDate)}";

    public static FuelEnergyLabels Labels(FuelEnergyKind kind) =>
        kind == FuelEnergyKind.Fuel
            ? new("Date", "Price per gallon", "Total gallons", "Total", "Leave empty to calculate it from the price and gallons.", "gal")
            : new("Date", "Price per kWh", "kWh delivered", "Total session cost", "Leave empty to calculate it from the price and kWh.", "kWh");

    // The kind travels in the form as text; anything unrecognized means fuel.
    public static FuelEnergyKind ParseKind(string? value) =>
        string.Equals(value, "energy", StringComparison.OrdinalIgnoreCase) ? FuelEnergyKind.Energy : FuelEnergyKind.Fuel;

    public static string KindValue(FuelEnergyKind kind) => kind == FuelEnergyKind.Energy ? "energy" : "fuel";
}
