using MyGarage.Shared.Drafts;

namespace MyGarage.Shared.VehicleDocuments;

// An original (PDF or image) the user kept with a vehicle; not a vehicle picture.
public sealed class VehicleDocumentDto
{
    public required Guid Id { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }

    // The record type it was kept with, when known.
    public DraftKind? SourceKind { get; init; }

    public Guid? SourceRecordId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
