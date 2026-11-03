namespace MyGarage.Shared.Notifications;

public sealed class UnreadCountDto
{
    public required int Unread { get; init; }

    public required int DraftsAwaitingReview { get; init; }
}
