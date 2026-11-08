namespace MyGarage.Shared.Reminders;

public sealed class ReminderDetailDto : ReminderDto
{
    // Most recent first.
    public required IReadOnlyList<ReminderCompletionDto> Completions { get; init; }
}
