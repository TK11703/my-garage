namespace MyGarage.Shared.Reminders;

// Nullable so the validator can report what is missing. At least one interval is required.
public sealed class ReminderRequest
{
    public string? Name { get; init; }

    public ReminderAppliesTo? AppliesTo { get; init; }

    // Required for equipment and location; must be empty for the vehicle.
    public string? Subject { get; init; }

    public int? IntervalMiles { get; init; }

    public ReminderTimeIntervalDto? IntervalTime { get; init; }

    public DateOnly? LastDoneOn { get; init; }

    // Required when IntervalMiles is set.
    public int? LastDoneMileage { get; init; }
}
