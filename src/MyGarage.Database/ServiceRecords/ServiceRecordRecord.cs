namespace MyGarage.Database.ServiceRecords;

public sealed record ServiceRecordRecord
{
    public Guid Id { get; init; }

    public Guid VehicleId { get; init; }

    public DateOnly DateServiced { get; init; }

    public int OdometerReading { get; init; }

    public string? ShopOrPerson { get; init; }

    public int LineItemCount { get; init; }

    public decimal TotalCost { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public string UpdatedBy { get; init; } = string.Empty;
}
