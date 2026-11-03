namespace MyGarage.Database.Drafts;

// One proposed record waiting for the owner. Proposal is JSON text that this project stores and returns but never interprets.
public sealed class MaintenanceDraftRecord
{
    public Guid Id { get; init; }

    public string OwnerId { get; init; } = string.Empty;

    public Guid SubmissionId { get; init; }

    public Guid? AttachmentId { get; init; }

    // needs_review, manual_entry, included, discarded, or expired.
    public string State { get; init; } = string.Empty;

    // Null only while the state is manual_entry.
    public string? Kind { get; init; }

    public Guid? VehicleId { get; init; }

    // vin, description, only_vehicle, or none.
    public string VehicleMatch { get; init; } = string.Empty;

    public decimal? Confidence { get; init; }

    public string Proposal { get; init; } = "{}";

    public string? Notes { get; init; }

    public string? CreatedRecordKind { get; init; }

    public Guid? CreatedRecordId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }
}

// What the processor hands over for one new draft; ids are assigned by the caller.
public sealed record NewMaintenanceDraft(
    Guid Id,
    string State,
    string? Kind,
    Guid? VehicleId,
    string VehicleMatch,
    decimal? Confidence,
    string ProposalJson,
    string? Notes,
    Guid? AttachmentId = null);

// An owner's vehicle as far as matching needs it.
public sealed class VehicleMatchCandidate
{
    public Guid Id { get; init; }

    public int? Year { get; init; }

    public string Make { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string? Vin { get; init; }
}
