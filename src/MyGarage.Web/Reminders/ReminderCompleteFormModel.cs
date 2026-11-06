namespace MyGarage.Web.Reminders;

// What the mark-done form binds to; the API validates it.
public sealed class ReminderCompleteFormModel
{
    public DateOnly? DoneOn { get; set; }

    public int? Mileage { get; set; }

    public bool UpdateVehicleMileage { get; set; }

    public MyGarage.Shared.Reminders.CompleteReminderRequest ToRequest() =>
        new() { DoneOn = DoneOn, Mileage = Mileage, UpdateVehicleMileage = UpdateVehicleMileage };
}
