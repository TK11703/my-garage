using System.Globalization;

namespace MyGarage.Web.Vehicles;

public static class VehicleFormat
{
    public static string Mileage(int? mileage) =>
        mileage is { } value ? $"{value.ToString("N0", CultureInfo.InvariantCulture)} mi" : "Mileage not set";
}
