namespace MyGarage.Shared.ServiceRecords;

// Either a flat cost (below zero for a discount), or labor and/or parts; never both.
// Labor is hours with an hourly rate (the total is calculated), or, with neither, an entered LaborCost total.
public sealed class ServiceLineItemRequest
{
    public string? Description { get; init; }

    public decimal? FlatCost { get; init; }

    public decimal? LaborHours { get; init; }

    public decimal? LaborRate { get; init; }

    // Ignored when both hours and rate are present.
    public decimal? LaborCost { get; init; }

    public IReadOnlyList<ServicePartRequest> Parts { get; init; } = [];
}
