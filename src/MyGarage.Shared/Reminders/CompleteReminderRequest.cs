namespace MyGarage.Shared.Reminders;

public sealed class CompleteReminderRequest
{
    public DateOnly? DoneOn { get; init; }

    // Required when the reminder has a distance interval.
    public int? Mileage { get; init; }

    // Honored only when Mileage is above the vehicle's current mileage.
    public bool UpdateVehicleMileage { get; init; }
}
