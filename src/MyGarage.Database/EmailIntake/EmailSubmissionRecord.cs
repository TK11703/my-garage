namespace MyGarage.Database.EmailIntake;

// Dapper maps columns to these init properties (underscores ignored); Status is set by the SQL that inserts the row.
public sealed class EmailSubmissionRecord
{
    public Guid Id { get; init; }

    public string MessageId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset ArrivedAt { get; init; }

    public DateTimeOffset? ReceivedAt { get; init; }

    public string? OwnerId { get; init; }

    public string? Sender { get; init; }

    public string? Subject { get; init; }

    public string? BodyText { get; init; }

    public short AttemptCount { get; init; }

    public DateTimeOffset NextAttemptAt { get; init; }

    public DateTimeOffset? LockedUntil { get; init; }

    public string? FailureReason { get; init; }

    // JSON array text of { fileName, reason } for attachments that were not stored.
    public string SkippedAttachments { get; init; } = "[]";

    public DateTimeOffset? ContentPurgedAt { get; init; }

    // email or upload; an upload has no sender, subject, or text.
    public string Origin { get; init; } = "email";

    // The owner's vehicle chosen when importing from a vehicle's maintenance tab; only an upload has one.
    public Guid? PreselectedVehicleId { get; init; }
}

public sealed class EmailAttachmentRecord
{
    public Guid Id { get; init; }

    public Guid SubmissionId { get; init; }

    public string OwnerId { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;

    public int SizeBytes { get; init; }

    public string Sha256 { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }
}

public enum AddSubmissionStatus
{
    Added,
    Duplicate,

    // The message id belonged to a failed submission that is now queued again with the new content.
    Requeued,
}

// SubmissionId is the new submission, or the existing one when the message id was already stored (Duplicate).
// A requeued submission takes the new id; the attachments it had before are removed and listed here so the caller deletes their blobs.
public sealed record AddSubmissionResult(AddSubmissionStatus Status, Guid SubmissionId)
{
    public Guid? PreviousSubmissionId { get; init; }

    public IReadOnlyList<Guid> RemovedAttachmentIds { get; init; } = [];
}
