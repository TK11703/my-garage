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
    public static string Interval(ReminderDto reminder)
    {
        var parts = new List<string>();
        if (reminder.IntervalMiles is { } miles)
        {
            parts.Add($"{Miles(miles)}");
        }

        if (reminder.IntervalTime is { } time)
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
