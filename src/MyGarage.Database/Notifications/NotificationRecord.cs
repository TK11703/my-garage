namespace MyGarage.Database.Notifications;

public sealed class NotificationRecord
{
    public Guid Id { get; init; }

    public string OwnerId { get; init; } = string.Empty;

    // Currently always draft_ready.
    public string Kind { get; init; } = string.Empty;

    public Guid? DraftId { get; init; }

    public string Title { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReadAt { get; init; }
}
