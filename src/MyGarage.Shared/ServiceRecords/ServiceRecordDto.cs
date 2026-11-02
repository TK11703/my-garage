namespace MyGarage.Shared.ServiceRecords;

public sealed class ServiceRecordDto
{
    public required Guid Id { get; init; }

    public required Guid VehicleId { get; init; }

    public required DateOnly DateServiced { get; init; }

    public required int OdometerReading { get; init; }

    public string? ShopOrPerson { get; init; }

    // In the order they were added.
    public required IReadOnlyList<ServiceLineItemDto> LineItems { get; init; }

    public required decimal TotalCost { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
