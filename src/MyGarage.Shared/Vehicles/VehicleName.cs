namespace MyGarage.Shared.Vehicles;

// A vehicle has no stored name; it is shown as "[Year] [Make] [Model]".
public static class VehicleName
{
    // Vehicles saved before the year was collected have no year, so it is left out.
    public static string Compose(int? year, string make, string model) =>
        year is { } value ? $"{value} {make} {model}" : $"{make} {model}";
}
