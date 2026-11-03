namespace MyGarage.Shared.Drafts;

public sealed class AcceptDraftRequest
{
    public Guid? VehicleId { get; init; }

    public DraftProposal? Proposal { get; init; }

    // Keep the attachment as a vehicle document linked to the new record.
    public bool KeepOriginal { get; init; }
}

public sealed class ChangeDraftKindRequest
{
    public DraftKind? Kind { get; init; }
}

public sealed class AcceptedDraftDto
{
    public required DraftKind Kind { get; init; }

    public required Guid VehicleId { get; init; }

    public required Guid RecordId { get; init; }

    public Guid? DocumentId { get; init; }
}
