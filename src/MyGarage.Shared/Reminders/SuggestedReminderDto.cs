namespace MyGarage.Shared.Reminders;

// A reminder the owner's own history suggests: a service done more than once with no reminder yet. Read-only; it is saved through the reminder form.
public class SuggestedReminderDto
{
    public required string Name { get; init; }

    public int? IntervalMiles { get; init; }

    public int? IntervalMonths { get; init; }

    public required DateOnly LastDoneOn { get; init; }

    public int? LastDoneMileage { get; init; }

    // A short sentence for the screen, for example "Done 3 times; last on Oct 30, 2025".
    public required string Basis { get; init; }
}
