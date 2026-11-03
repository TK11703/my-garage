namespace MyGarage.Shared.ServiceRecords;

public sealed class ServiceRecordSummaryDto
{
    public required Guid Id { get; init; }

    public required DateOnly DateServiced { get; init; }

    public required int OdometerReading { get; init; }

    public string? ShopOrPerson { get; init; }

    public required int LineItemCount { get; init; }

    public required decimal TotalCost { get; init; }

    public bool FromEmail { get; init; }
}
