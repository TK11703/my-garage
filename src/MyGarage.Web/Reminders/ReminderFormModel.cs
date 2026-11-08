using MyGarage.Shared.Reminders;

namespace MyGarage.Web.Reminders;

// What the add and edit forms bind to; the API validates it.
public sealed class ReminderFormModel
{
    public string? Name { get; set; }

    public int? IntervalMiles { get; set; }

    public int? TimeValue { get; set; }

    public ReminderTimeUnit TimeUnit { get; set; } = ReminderTimeUnit.Months;

    // The select binds to this lower-case text; an unknown value falls back to months.
    public string TimeUnitText
    {
        get => TimeUnit == ReminderTimeUnit.Years ? "years" : "months";
        set => TimeUnit = value == "years" ? ReminderTimeUnit.Years : ReminderTimeUnit.Months;
    }
    public DateOnly? LastDoneOn { get; set; }

    public int? LastDoneMileage { get; set; }

    public ReminderRequest ToRequest() =>
        new()
        {
            Name = Name,
            IntervalMiles = IntervalMiles,
            IntervalTime = TimeValue is { } value ? new ReminderTimeIntervalDto { Value = value, Unit = TimeUnit } : null,
            LastDoneOn = LastDoneOn,
            LastDoneMileage = LastDoneMileage,
        };

    // A new reminder starts from the chosen preset (or empty for custom), last done today at the vehicle's current mileage.
    public static ReminderFormModel ForNew(ReminderPreset? preset, DateOnly today, int? currentMileage) =>
        new()
        {
            Name = preset?.Name,
            IntervalMiles = preset?.IntervalMiles,
            TimeValue = preset?.IntervalTime?.Value,
            TimeUnit = preset?.IntervalTime?.Unit ?? ReminderTimeUnit.Months,
            LastDoneOn = today,
            LastDoneMileage = currentMileage,
        };

    public static ReminderFormModel From(ReminderDto reminder) =>
        new()
        {
            Name = reminder.Name,
            IntervalMiles = reminder.IntervalMiles,
            TimeValue = reminder.IntervalTime?.Value,
            TimeUnit = reminder.IntervalTime?.Unit ?? ReminderTimeUnit.Months,
            LastDoneOn = reminder.LastDoneOn,
            LastDoneMileage = reminder.LastDoneMileage,
        };
}
