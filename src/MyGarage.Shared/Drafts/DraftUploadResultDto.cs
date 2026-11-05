namespace MyGarage.Shared.Drafts;

// The answer to an upload: how many files were queued and which were refused.
public sealed class DraftUploadResultDto
{
    public required Guid SubmissionId { get; init; }

    public required int AcceptedCount { get; init; }

    public IReadOnlyList<DraftUploadRefusalDto> Refused { get; init; } = [];
}

// Index is the file's position in the request; Reason is one of the DraftUploadLimits codes.
public sealed class DraftUploadRefusalDto
{
    public required int Index { get; init; }

    public required string Reason { get; init; }
}
