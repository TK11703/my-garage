namespace MyGarage.Shared.Reminders;

public sealed record ReminderPreset(string Id, string Name, int? IntervalMiles, ReminderTimeIntervalDto? IntervalTime);

// Typical starting values the add form offers; the user can change every one of them.
public static class ReminderPresets
{
    public static readonly IReadOnlyList<ReminderPreset> All =
    [
        new("oil-change", "Oil change", 5_000, new ReminderTimeIntervalDto { Value = 6, Unit = ReminderTimeUnit.Months }),
        new("tire-rotation", "Tire rotation", 7_500, null),
        new("inspection", "Inspection", null, new ReminderTimeIntervalDto { Value = 1, Unit = ReminderTimeUnit.Years }),
        new("registration", "Registration", null, new ReminderTimeIntervalDto { Value = 1, Unit = ReminderTimeUnit.Years }),
    ];

    // Null for custom or an unknown id.
    public static ReminderPreset? Find(string? id) => All.FirstOrDefault(preset => preset.Id == id);
}
