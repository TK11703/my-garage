using System.Globalization;
using MyGarage.Shared.Reminders;
using MyGarage.Web.Common;

namespace MyGarage.Web.Reminders;

public sealed record RemainingPart(string Text, bool Urgent);

public static class ReminderFormat
{
    // "1,200 miles left", "due in 18 days", "400 miles overdue", joined for a reminder with both parts.
    public static string Remaining(ReminderDto reminder) => string.Join(" · ", RemainingParts(reminder).Select(part => part.Text));

    // Urgent marks the part at or past its Due soon limit, but only when the reminder has both a distance and a time interval.
    public static IReadOnlyList<RemainingPart> RemainingParts(ReminderDto reminder)
    {
        var both = reminder.IntervalMiles is not null && reminder.IntervalTime is not null;
        var parts = new List<RemainingPart>();
        if (reminder.MilesRemaining is { } miles)
        {
            parts.Add(new RemainingPart(MilesRemaining(miles), both && miles <= ReminderThresholds.DueSoonMiles));
        }

        if (reminder.DaysRemaining is { } days)
        {
            parts.Add(new RemainingPart(DaysRemaining(days), both && days <= ReminderThresholds.DueSoonDays));
        }

        return parts;
    }

    // How much of the interval is already used, 0 to 100, from the more used of the distance and time parts; null for a paused or archived reminder or one with nothing to measure.
    public static int? PercentUsed(ReminderDto reminder)
    {
        if (reminder.Status is ReminderStatus.Paused or ReminderStatus.Archived)
        {
            return null;
        }

        double? used = null;
        if (reminder.IntervalMiles is > 0 and var intervalMiles && reminder.MilesRemaining is { } miles)
        {
            used = 1d - ((double)miles / intervalMiles);
        }

        if (reminder.IntervalTime is { Value: > 0 } time && reminder.DaysRemaining is { } days)
        {
            var intervalDays = time.Value * (time.Unit == ReminderTimeUnit.Years ? 365.25 : 30.4375);
            var usedTime = 1d - (days / intervalDays);
            used = used is null ? usedTime : Math.Max(used.Value, usedTime);
        }

        return used is null ? null : (int)Math.Round(Math.Clamp(used.Value, 0d, 1d) * 100d, MidpointRounding.AwayFromZero);
    }

    // "45,000 miles or Nov 1, 2026, whichever comes first": the first due point from the form values so far. Keep wwwroot/js/reminder-preview.js in step.
    public static string NextDuePreview(int? intervalMiles, int? timeValue, ReminderTimeUnit unit, DateOnly? lastDoneOn, int? lastDoneMileage)
    {
        string? miles = intervalMiles is > 0 and <= 999_999 && lastDoneMileage is >= 0 and <= 9_999_999 and var done ? Miles(done + intervalMiles.Value) : null;
        string? date = timeValue is > 0 and <= 600 && lastDoneOn is { } on && AddInterval(on, timeValue.Value, unit) is { } due ? DisplayFormat.Date(due) : null;

        return (miles, date) switch
        {
            (not null, not null) => $"{miles} or {date}, whichever comes first",
            (not null, null) => miles,
            (null, not null) => date,
            _ => "Enter how often it repeats and when it was last done to see when it is next due.",
        };
    }

    // Null when the result is past the last supported date.
    private static DateOnly? AddInterval(DateOnly from, int value, ReminderTimeUnit unit)
    {
        try
        {
            return unit == ReminderTimeUnit.Years ? from.AddYears(value) : from.AddMonths(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string MilesRemaining(int remaining) => remaining switch
    {
        0 => "due now",
        > 0 => $"{Miles(remaining)} left",
        _ => $"{Miles(-remaining)} overdue",
    };

    private static string DaysRemaining(int remaining) => remaining switch
    {
        0 => "due today",
        > 0 => $"due in {remaining.ToString(CultureInfo.InvariantCulture)} day{(remaining == 1 ? string.Empty : "s")}",
        _ => $"{(-remaining).ToString(CultureInfo.InvariantCulture)} day{(remaining == -1 ? string.Empty : "s")} overdue",
    };

    // "Every 5,000 miles or 6 months".
    public static string Interval(ReminderDto reminder) => Interval(reminder.IntervalMiles, reminder.IntervalTime);

    public static string Interval(SuggestedReminderDto suggestion) =>
        Interval(suggestion.IntervalMiles, suggestion.IntervalMonths is { } months ? new ReminderTimeIntervalDto { Value = months, Unit = ReminderTimeUnit.Months } : null);

    private static string Interval(int? intervalMiles, ReminderTimeIntervalDto? intervalTime)
    {
        var parts = new List<string>();
        if (intervalMiles is { } miles)
        {
            parts.Add($"{Miles(miles)}");
        }

        if (intervalTime is { } time)
        {
            var unit = time.Unit == ReminderTimeUnit.Years ? "year" : "month";
            parts.Add($"{time.Value.ToString(CultureInfo.InvariantCulture)} {unit}{(time.Value == 1 ? string.Empty : "s")}");
        }

        return "Every " + string.Join(" or ", parts);
    }

    // "May 1, 2026 at 40,000 miles".
    public static string LastDone(ReminderDto reminder) =>
        reminder.LastDoneMileage is { } mileage
            ? $"{DisplayFormat.Date(reminder.LastDoneOn)} at {Miles(mileage)}"
            : DisplayFormat.Date(reminder.LastDoneOn);

    // "45,000 miles or Nov 1, 2026": it is due at whichever comes first.
    public static string NextDue(ReminderDto reminder)
    {
        var parts = new List<string>();
        if (reminder.NextDueMileage is { } mileage)
        {
            parts.Add(Miles(mileage));
        }

        if (reminder.NextDueDate is { } date)
        {
            parts.Add(DisplayFormat.Date(date));
        }

        return string.Join(" or ", parts);
    }

    // "45,000 miles".
    public static string Miles(int miles) =>
        $"{miles.ToString("N0", CultureInfo.InvariantCulture)} {(Math.Abs(miles) == 1 ? "mile" : "miles")}";
}
