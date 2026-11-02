using System.Globalization;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Web.Vehicles;

public static class VehicleFormat
{
    public const string NotSet = "Not set";

    public static string Mileage(int? mileage) =>
        mileage is { } value ? $"{value.ToString("N0", CultureInfo.InvariantCulture)} mi" : "Mileage not set";

    public static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? NotSet : value;

    public static string Number(int? value) => value is { } number ? number.ToString(CultureInfo.InvariantCulture) : NotSet;

    public static string MileageOrNotSet(int? mileage) => mileage is null ? NotSet : Mileage(mileage);

    public static string Horsepower(int? horsepower) => horsepower is { } value ? $"{value.ToString(CultureInfo.InvariantCulture)} hp" : NotSet;

    public static string WheelSize(decimal? size) => size is { } value ? $"{value.ToString("0.#", CultureInfo.InvariantCulture)} in" : NotSet;

    public static string Date(DateOnly? date) => date is { } value ? value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) : NotSet;

    public static string Ownership(OwnershipType? ownership) => ownership?.ToString() ?? NotSet;
}
