using MyGarage.Shared.Reminders;

namespace MyGarage.Api.Reminders;

// Limits shared by the reminder validators; they mirror the CHECK constraints of migration 0016.
public static class ReminderRules
{
    public const int MaxNameLength = 100;

    public const int MaxSubjectLength = 100;

    public const int MaxIntervalMiles = 999_999;

    public const int MaxIntervalMonths = 600;

    public const int MaxIntervalYears = 50;

    public const int MaxMileage = 9_999_999;

    public static readonly DateOnly EarliestDate = new(1900, 1, 1);

    public static DateOnly Today(TimeProvider timeProvider) => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    public static bool IsValidDoneDate(DateOnly date, TimeProvider timeProvider) => date >= EarliestDate && date <= Today(timeProvider);

    public static bool TimeValueInRange(ReminderTimeUnit unit, int value) =>
        unit == ReminderTimeUnit.Years ? value is >= 1 and <= MaxIntervalYears : value is >= 1 and <= MaxIntervalMonths;
}
