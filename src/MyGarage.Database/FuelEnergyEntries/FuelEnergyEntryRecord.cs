namespace MyGarage.Database.FuelEnergyEntries;

public sealed record FuelEnergyEntryRecord
{
    public Guid Id { get; init; }

    public Guid VehicleId { get; init; }

    // Stored text: "fuel" or "energy".
    public string Kind { get; init; } = string.Empty;

    public DateOnly EntryDate { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal Quantity { get; init; }

    public decimal TotalCost { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public string UpdatedBy { get; init; } = string.Empty;
}
