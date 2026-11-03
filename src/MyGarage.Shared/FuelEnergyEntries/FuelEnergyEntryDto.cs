namespace MyGarage.Shared.FuelEnergyEntries;

public sealed class FuelEnergyEntryDto
{
    public required Guid Id { get; init; }

    public required Guid VehicleId { get; init; }

    public required FuelEnergyKind Kind { get; init; }

    public required DateOnly EntryDate { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal TotalCost { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    // True when the entry was created from an emailed draft.
    public bool FromEmail { get; init; }
}
