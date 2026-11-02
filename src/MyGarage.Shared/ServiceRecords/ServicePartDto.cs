namespace MyGarage.Shared.ServiceRecords;

public sealed class ServicePartDto
{
    public required string Name { get; init; }

    public required decimal Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal TotalPrice { get; init; }
}
