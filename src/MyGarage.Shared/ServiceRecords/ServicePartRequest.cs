namespace MyGarage.Shared.ServiceRecords;

public sealed class ServicePartRequest
{
    public string? Name { get; init; }

    public decimal? Quantity { get; init; }

    public decimal? UnitPrice { get; init; }
}
