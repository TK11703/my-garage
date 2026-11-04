namespace MyGarage.Shared.ServiceRecords;

public sealed class ServiceRecordRequest
{
    public DateOnly? DateServiced { get; init; }

    public int? OdometerReading { get; init; }

    public string? ShopOrPerson { get; init; }

    // Optional tax for the whole record, separate from the line items; empty or zero means none.
    public decimal? Tax { get; init; }
}
