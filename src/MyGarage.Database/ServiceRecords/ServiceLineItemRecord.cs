namespace MyGarage.Database.ServiceRecords;

public sealed record ServiceLineItemRecord
{
    public Guid Id { get; init; }

    public Guid ServiceRecordId { get; init; }

    public string Description { get; init; } = string.Empty;

    public decimal? FlatCost { get; init; }

    public decimal? LaborHours { get; init; }

    public decimal? LaborRate { get; init; }

    // The entered labor total; null when labor is calculated from hours and rate or absent.
    public decimal? LaborCost { get; init; }

    public decimal TotalCost { get; init; }

    public IReadOnlyList<ServiceLinePartRecord> Parts { get; init; } = [];
}
