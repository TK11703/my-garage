namespace MyGarage.Web.Vehicles;

// Supplied by the vehicle page to every tab so sections can leave out the controls that change an archived vehicle.
public sealed record VehicleArchivedState(bool IsArchived)
{
    public static VehicleArchivedState Active { get; } = new(false);

    public static VehicleArchivedState Archived { get; } = new(true);
}
