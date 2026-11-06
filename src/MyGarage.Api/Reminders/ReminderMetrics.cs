using System.Diagnostics.Metrics;

namespace MyGarage.Api.Reminders;

// Counters only: states and counts, never reminder details.
public static class ReminderMetrics
{
    public const string MeterName = "MyGarage.Reminders";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Evaluated =
        Meter.CreateCounter<long>("reminders_evaluated", description: "Active reminders the notifier looked at.");

    // Tagged state = due_soon or overdue.
    public static readonly Counter<long> Notified =
        Meter.CreateCounter<long>("reminders_notified", description: "Notifications raised because a reminder reached a new state.");
}
