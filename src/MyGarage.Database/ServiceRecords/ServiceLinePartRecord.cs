namespace MyGarage.Database.ServiceRecords;

public sealed record ServiceLinePartRecord
{
    public Guid LineItemId { get; init; }

    public int Position { get; init; }

    public string Name { get; init; } = string.Empty;

    public decimal Quantity { get; init; }

    public decimal UnitPrice { get; init; }
}
