namespace MyGarage.Web.Vehicles;

public static class VehicleMessages
{
    public const string Unavailable = "The My Garage service is not reachable right now. Please try again shortly.";
    public const string NotAccepted = "The service did not accept your sign-in. Please sign in again.";
    public const string NotFound = "This vehicle could not be found.";
    public const string ArchivedCannotChange = "This vehicle is archived and cannot be changed.";
    public const string Archived = "Vehicle archived. Find it under Archived in My Vehicles.";
    public const string Restored = "Vehicle restored.";
    public const string AlreadyArchived = "This vehicle was already archived.";
    public const string AlreadyActive = "This vehicle was already active.";
    public const string NoActiveVehicles = "You have no active vehicles.";
    public const string NoArchivedVehicles = "No archived vehicles. Archive a vehicle you no longer own to keep its history without counting it in your garage.";

    // The text for a failed API call: sign-in, archived vehicle, or an outage.
    public static string For(MyGarage.Web.Common.GarageApiFailure failure) => failure switch
    {
        MyGarage.Web.Common.GarageApiFailure.NotAccepted => NotAccepted,
        MyGarage.Web.Common.GarageApiFailure.VehicleArchived => ArchivedCannotChange,
        _ => Unavailable,
    };

    // The calendar day (UTC) the vehicle was archived, in the app's date style.
    public static string ArchivedOn(DateTimeOffset archivedAt) => Common.DisplayFormat.Date(DateOnly.FromDateTime(archivedAt.UtcDateTime));

    public static string? SavedNotice(string? saved) => saved switch
    {
        "archived" => Archived,
        "restored" => Restored,
        _ => null,
    };
}
