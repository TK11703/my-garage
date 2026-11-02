namespace MyGarage.Shared.ServiceRecords;

// Either a flat cost, or labor (hours with an hourly rate) and/or parts; never both.
public sealed class ServiceLineItemRequest
{
    public string? Description { get; init; }

    public decimal? FlatCost { get; init; }

    public decimal? LaborHours { get; init; }

    public decimal? LaborRate { get; init; }

    public IReadOnlyList<ServicePartRequest> Parts { get; init; } = [];
}
