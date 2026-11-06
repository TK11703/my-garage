namespace MyGarage.Database.Reminders;

// An active reminder with its owner, for the notifier.
public sealed record ReminderForEvaluationRecord : ReminderRecord
{
    public string OwnerId { get; init; } = string.Empty;
}
