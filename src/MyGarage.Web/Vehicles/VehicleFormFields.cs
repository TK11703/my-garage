namespace MyGarage.Web.Vehicles;

public static class VehicleFormFields
{
    // Which edit tab each field lives on; used to open the tab that holds an error.
    public static readonly string[] BasicFields =
        ["Nickname", "Vin", "Make", "Model", "Trim", "Year", "VehicleTypeId", "Ownership", "PurchasedOn", "LeaseEndsOn"];

    public static readonly string[] SpecificationFields =
        ["Engine", "Horsepower", "FuelTypeId", "TransmissionId", "Mileage", "Color", "Seats", "Doors", "DrivetrainId", "WheelSize", "TireSize"];

    public static string? Described(IReadOnlyDictionary<string, string[]> errors, string field) =>
        errors.Keys.Any(key => string.Equals(key, field, StringComparison.OrdinalIgnoreCase)) ? $"{field.ToLowerInvariant()}-error" : null;

    public static bool HasError(IReadOnlyDictionary<string, string[]> errors, IEnumerable<string> fields) =>
        fields.Any(field => errors.Keys.Any(key => string.Equals(key, field, StringComparison.OrdinalIgnoreCase)));
}
