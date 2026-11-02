namespace MyGarage.Shared.ServiceRecords;

public sealed class ServiceLineItemDto
{
    public required Guid Id { get; init; }

    public required string Description { get; init; }

    public decimal? FlatCost { get; init; }

    public decimal? LaborHours { get; init; }

    public decimal? LaborRate { get; init; }

    // Hours times rate, rounded to two decimals; null without labor.
    public decimal? LaborCost { get; init; }

    public required IReadOnlyList<ServicePartDto> Parts { get; init; }

    public required decimal TotalCost { get; init; }
}
