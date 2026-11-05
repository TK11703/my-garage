namespace MyGarage.Shared.Drafts;

// One draft with everything the review page shows. Email text is untrusted: render it escaped.
public sealed class MaintenanceDraftDto
{
    public required Guid Id { get; init; }

    public required DraftState State { get; init; }

    // Null only while the draft is waiting for the user to choose a record type.
    public DraftKind? Kind { get; init; }

    public required DraftProposal Proposal { get; init; }

    public required DraftVehicleDto Vehicle { get; init; }

    public required DraftSourceDto Source { get; init; }

    // Whether the draft began as an email or as a file the user uploaded.
    public DraftOrigin Origin { get; init; } = DraftOrigin.Email;

    // Null once the email content has been removed (the draft was decided or expired). For an upload it holds only the upload time.
    public DraftEmailDto? Email { get; init; }

    public required CompatibilityDto Compatibility { get; init; }

    public decimal? Confidence { get; init; }

    public string? Notes { get; init; }

    public required bool HasAttachment { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed class DraftVehicleDto
{
    public Guid? VehicleId { get; init; }

    public required VehicleMatch Match { get; init; }

    // The caller's own vehicles, for the picker.
    public required IReadOnlyList<VehicleSuggestionDto> Candidates { get; init; }
}

public sealed class VehicleSuggestionDto
{
    public required Guid VehicleId { get; init; }

    public required string Name { get; init; }

    public required VehicleMatch Match { get; init; }
}

// "text" when the draft was read from the email body, "attachment" when from a stored file.
public sealed class DraftSourceDto
{
    public required string Kind { get; init; }

    public string? FileName { get; init; }

    public string? ContentType { get; init; }

    public long? SizeBytes { get; init; }
}

public sealed class DraftEmailDto
{
    public required DateTimeOffset ArrivedAt { get; init; }

    public string? Sender { get; init; }

    public string? Subject { get; init; }
}
