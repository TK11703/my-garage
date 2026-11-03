namespace MyGarage.Api.FuelEnergyEntries;

public static class FuelEnergyTotal
{
    // A total the user entered is kept as entered; otherwise it is the price times the amount, rounded to the cent.
    public static decimal Resolve(decimal unitPrice, decimal quantity, decimal? totalCost) =>
        totalCost ?? decimal.Round(unitPrice * quantity, 2, MidpointRounding.AwayFromZero);
}
