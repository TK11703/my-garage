using MyGarage.Shared.Reminders;

namespace MyGarage.Api.Reminders;

public static class ReminderOrdering
{
    // Overdue, Due soon, Mileage needed, Upcoming, Paused, Archived; then earliest due date, fewest miles left, name, id. Missing values sort last.
    public static IReadOnlyList<ReminderDto> MostUrgentFirst(IEnumerable<ReminderDto> reminders) =>
        [.. reminders
            .OrderBy(reminder => Rank(reminder.Status))
            .ThenBy(reminder => reminder.NextDueDate is null ? 1 : 0)
            .ThenBy(reminder => reminder.NextDueDate)
            .ThenBy(reminder => reminder.MilesRemaining is null ? 1 : 0)
            .ThenBy(reminder => reminder.MilesRemaining)
            .ThenBy(reminder => reminder.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reminder => reminder.Name, StringComparer.Ordinal)
            .ThenBy(reminder => reminder.Id)];

    private static int Rank(ReminderStatus status) => status switch
    {
        ReminderStatus.Overdue => 0,
        ReminderStatus.DueSoon => 1,
        ReminderStatus.MileageNeeded => 2,
        ReminderStatus.Upcoming => 3,
        ReminderStatus.Paused => 4,
        _ => 5,
    };
}
