namespace MyGarage.Shared.Vehicles;

// One dated entry of a vehicle's history, for the Timeline tab.
public sealed class VehicleTimelineEntryDto
{
    public required VehicleTimelineKind Kind { get; init; }

    // Id of the source record.
    public required Guid Id { get; init; }

    public required DateOnly OccurredOn { get; init; }

    public required string Title { get; init; }

    public string? Detail { get; init; }

    public decimal? Amount { get; init; }

    public int? OdometerReading { get; init; }

    // Created from an email or an upload draft.
    public bool FromEmail { get; init; }
}
