namespace MyGarage.Shared.Vehicles;

// Which vehicles the list shows; absent means Active.
public static class VehicleStatusFilter
{
    public const string Active = "active";

    public const string Archived = "archived";

    public const string All = "all";

    public static bool IsValid(string? value) => value is null or Active or Archived or All;
}
