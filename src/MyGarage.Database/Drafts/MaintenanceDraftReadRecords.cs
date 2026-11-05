namespace MyGarage.Database.Drafts;

// One row of the review list: the draft, its vehicle name parts, and when the email arrived.
public sealed class MaintenanceDraftSummaryRecord
{
    public Guid Id { get; init; }

    public string State { get; init; } = string.Empty;

    public string? Kind { get; init; }

    public Guid? VehicleId { get; init; }

    public int? VehicleYear { get; init; }

    public string? VehicleMake { get; init; }

    public string? VehicleModel { get; init; }

    // JSON text the Api reads for the headline total; never interpreted here.
    public string Proposal { get; init; } = "{}";

    public DateTimeOffset ArrivedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    // email or upload.
    public string Origin { get; init; } = "email";
}

// A draft with the retained email details and attachment metadata; the email columns are null once the content was purged.
public sealed class MaintenanceDraftDetailRecord
{
    public Guid Id { get; init; }

    public Guid SubmissionId { get; init; }

    public Guid? AttachmentId { get; init; }

    public string State { get; init; } = string.Empty;

    public string? Kind { get; init; }

    public Guid? VehicleId { get; init; }

    public string VehicleMatch { get; init; } = string.Empty;

    public decimal? Confidence { get; init; }

    public string Proposal { get; init; } = "{}";

    public string? Notes { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset ArrivedAt { get; init; }

    public string? Sender { get; init; }

    public string? Subject { get; init; }

    // email or upload.
    public string Origin { get; init; } = "email";

    public string? AttachmentFileName { get; init; }

    public string? AttachmentContentType { get; init; }

    public int? AttachmentSizeBytes { get; init; }

    public string? AttachmentSha256 { get; init; }
}

// The stored attachment of a draft that was just decided or expired; the caller deletes its file.
public sealed record DraftAttachmentRef(Guid SubmissionId, Guid AttachmentId);

// The columns the guarded decision statement returns.
internal sealed class DecidedDraft
{
    public Guid SubmissionId { get; init; }

    public Guid? AttachmentId { get; init; }
}

public enum DraftDecisionOutcome
{
    Done,
    NotFound,
    AlreadyDecided,
}

public sealed record DiscardDraftResult(DraftDecisionOutcome Outcome, DraftAttachmentRef? Attachment = null);

// Count is every draft that was expired; Attachments are the stored files of those that had one.
public sealed record ExpireDueResult(int Count, IReadOnlyList<DraftAttachmentRef> Attachments);
