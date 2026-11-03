namespace MyGarage.Shared.Notifications;

public sealed class NotificationDto
{
    public required Guid Id { get; init; }

    // Currently always draft_ready.
    public required string Kind { get; init; }

    public required string Title { get; init; }

    public Guid? DraftId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required bool IsRead { get; init; }
}
