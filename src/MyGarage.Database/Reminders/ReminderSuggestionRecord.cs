namespace MyGarage.Database.Reminders;

// A service the owner has done more than once with no reminder yet, and the interval its history suggests.
public sealed record ReminderSuggestionRecord
{
    public string Name { get; init; } = string.Empty;

    public int? IntervalMiles { get; init; }

    public int? IntervalMonths { get; init; }

    public DateOnly LastDoneOn { get; init; }

    public int? LastDoneMileage { get; init; }

    // How many service records the service appears in.
    public int Times { get; init; }
}
