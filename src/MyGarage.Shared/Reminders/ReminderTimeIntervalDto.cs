namespace MyGarage.Shared.Reminders;

public sealed class ReminderTimeIntervalDto
{
    // Months 1 to 600 or years 1 to 50.
    public required int Value { get; init; }

    public required ReminderTimeUnit Unit { get; init; }
}
