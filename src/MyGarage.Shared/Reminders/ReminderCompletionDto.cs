namespace MyGarage.Shared.Reminders;

public sealed class ReminderCompletionDto
{
    public required Guid Id { get; init; }

    public required DateOnly DoneOn { get; init; }

    public int? Mileage { get; init; }
}
