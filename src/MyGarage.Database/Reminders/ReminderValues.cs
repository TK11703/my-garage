namespace MyGarage.Database.Reminders;

// What the owner submits when adding or editing a reminder; AppliesTo is vehicle, equipment, or location.
public sealed record ReminderValues(
    string Name,
    string AppliesTo,
    string? Subject,
    int? IntervalMiles,
    int? IntervalMonths,
    DateOnly LastDoneOn,
    int? LastDoneMileage);
