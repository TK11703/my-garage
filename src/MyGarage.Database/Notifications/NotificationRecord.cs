namespace MyGarage.Database.Notifications;

public sealed class NotificationRecord
{
    public Guid Id { get; init; }

    public string OwnerId { get; init; } = string.Empty;

    // draft_ready, reminder_due_soon, or reminder_overdue.
    public string Kind { get; init; } = string.Empty;

    public Guid? DraftId { get; init; }

    public Guid? ReminderId { get; init; }

    // The reminder's vehicle; set for reminder notifications only.
    public Guid? VehicleId { get; init; }

    public string Title { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReadAt { get; init; }
}
