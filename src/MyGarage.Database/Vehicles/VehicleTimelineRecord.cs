namespace MyGarage.Database.Vehicles;

// One dated entry of a vehicle's history. Kind is the stored text: service, fuel-energy, inspection, registration, insurance, tax, or journal.
public sealed record VehicleTimelineRecord
{
    public string Kind { get; init; } = string.Empty;

    public Guid Id { get; init; }

    public DateOnly OccurredOn { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Detail { get; init; }

    public decimal? Amount { get; init; }

    public int? OdometerReading { get; init; }

    public bool FromEmail { get; init; }
}
