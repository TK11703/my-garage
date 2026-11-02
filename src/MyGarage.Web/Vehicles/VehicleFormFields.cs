namespace MyGarage.Web.Vehicles;

public static class VehicleFormFields
{
    // Which edit tab each field lives on; used to open the tab that holds an error.
    public static readonly string[] BasicFields =
        ["Year", "Make", "Model", "Trim", "Vin", "VehicleTypeId", "Ownership", "PurchasedOn", "LeaseEndsOn", "Mileage", "LatestOdometerReading"];

    public static readonly string[] SpecificationFields =
        ["Engine", "Horsepower", "DrivetrainId", "Color", "FuelTypeId", "TransmissionId", "Seats", "Doors", "WheelSize", "TireSize"];

    public static string? Described(IReadOnlyDictionary<string, string[]> errors, string field) =>
        errors.Keys.Any(key => string.Equals(key, field, StringComparison.OrdinalIgnoreCase)) ? $"{field.ToLowerInvariant()}-error" : null;

    public static bool HasError(IReadOnlyDictionary<string, string[]> errors, IEnumerable<string> fields) =>
        fields.Any(field => errors.Keys.Any(key => string.Equals(key, field, StringComparison.OrdinalIgnoreCase)));
}
