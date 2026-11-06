using System.Globalization;
using MyGarage.Shared.Reminders;
using MyGarage.Web.Common;

namespace MyGarage.Web.Reminders;

public static class ReminderFormat
{
    // "Vehicle", "Equipment: Trailer hitch", or "Location: Home garage".
    public static string AppliesTo(ReminderDto reminder) => reminder.AppliesTo switch
    {
        ReminderAppliesTo.Equipment => $"Equipment: {reminder.Subject}",
        ReminderAppliesTo.Location => $"Location: {reminder.Subject}",
        _ => "Vehicle",
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
