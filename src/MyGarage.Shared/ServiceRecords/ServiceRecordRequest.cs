namespace MyGarage.Shared.ServiceRecords;

public sealed class ServiceRecordRequest
{
    public DateOnly? DateServiced { get; init; }

    public int? OdometerReading { get; init; }

    public string? ShopOrPerson { get; init; }
}
