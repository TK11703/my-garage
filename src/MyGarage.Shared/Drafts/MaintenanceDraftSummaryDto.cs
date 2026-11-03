namespace MyGarage.Shared.Drafts;

public sealed class MaintenanceDraftSummaryDto
{
    public required Guid Id { get; init; }

    public required DraftState State { get; init; }

    public DraftKind? Kind { get; init; }

    public string? VehicleName { get; init; }

    public required DateTimeOffset ArrivedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    // The proposed total, amount, or cost when the proposal has one.
    public decimal? HeadlineTotal { get; init; }
}
