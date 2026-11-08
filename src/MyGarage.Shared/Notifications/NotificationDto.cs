namespace MyGarage.Shared.Notifications;

public sealed class NotificationDto
{
    public required Guid Id { get; init; }

    // draft_ready, reminder_due_soon, or reminder_overdue.
    public required string Kind { get; init; }

    public required string Title { get; init; }

    public Guid? DraftId { get; init; }

    // Set for reminder notifications only.
    public Guid? ReminderId { get; init; }

    // The reminder's vehicle; set for reminder notifications only.
    public Guid? VehicleId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required bool IsRead { get; init; }
}
