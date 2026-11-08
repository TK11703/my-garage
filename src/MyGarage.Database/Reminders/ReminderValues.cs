namespace MyGarage.Database.Reminders;

// What the owner submits when adding or editing a reminder.
public sealed record ReminderValues(
    string Name,
    int? IntervalMiles,
    int? IntervalMonths,
    DateOnly LastDoneOn,
    int? LastDoneMileage);
