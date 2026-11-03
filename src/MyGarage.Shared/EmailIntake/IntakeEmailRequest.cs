namespace MyGarage.Shared.EmailIntake;

// One transformed email handed over by the trusted workflow.
public sealed class IntakeEmailRequest
{
    public string? MessageId { get; init; }

    public DateTimeOffset? ReceivedAt { get; init; }

    public string? From { get; init; }

    public string? Subject { get; init; }

    public string? BodyText { get; init; }

    public IReadOnlyList<IntakeAttachmentRequest> Attachments { get; init; } = [];
}
