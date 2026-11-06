namespace MyGarage.Database.Reminders;

public sealed record ReminderCompletionRecord
{
    public Guid Id { get; init; }

    public Guid ReminderId { get; init; }

    public DateOnly DoneOn { get; init; }

    public int? Mileage { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
