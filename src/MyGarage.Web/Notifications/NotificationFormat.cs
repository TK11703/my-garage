using System.Globalization;
using MyGarage.Web.Common;

namespace MyGarage.Web.Notifications;

public static class NotificationFormat
{
    // "just now", "5 minutes ago", "3 hours ago", "2 days ago", then the date.
    public static string Relative(DateTimeOffset createdAt, DateTimeOffset now)
    {
        var age = now - createdAt;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return Ago((int)age.TotalMinutes, "minute");
        }

        if (age < TimeSpan.FromDays(1))
        {
            return Ago((int)age.TotalHours, "hour");
        }

        return age < TimeSpan.FromDays(7)
            ? Ago((int)age.TotalDays, "day")
            : DisplayFormat.Date(DateOnly.FromDateTime(createdAt.UtcDateTime));
    }

    private static string Ago(int count, string unit) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} {unit}{(count == 1 ? string.Empty : "s")} ago";
}
