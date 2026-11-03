using System.Text.Json.Serialization;
using MyGarage.Shared.Drafts;

namespace MyGarage.Shared.EmailIntake;

// What became of one email attributed to the caller. It never carries the sender, subject, or text.
public sealed class EmailHistoryItemDto
{
    public required Guid SubmissionId { get; init; }

    public required DateTimeOffset ArrivedAt { get; init; }

    public required EmailHistoryOutcome Outcome { get; init; }

    // The records created from this email (included drafts only).
    public IReadOnlyList<EmailHistoryRecordDto> Records { get; init; } = [];
}

public sealed class EmailHistoryRecordDto
{
    public required DraftKind Kind { get; init; }

    public required Guid VehicleId { get; init; }

    public required Guid RecordId { get; init; }
}

// JSON text: awaiting_review, included, discarded, expired, not_applicable, failed.
[JsonConverter(typeof(SnakeCaseEnumConverter<EmailHistoryOutcome>))]
public enum EmailHistoryOutcome
{
    AwaitingReview,
    Included,
    Discarded,
    Expired,
    NotApplicable,
    Failed,
}
