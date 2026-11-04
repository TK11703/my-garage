using MyGarage.Shared.ServiceRecords;

namespace MyGarage.Web.ServiceRecords;

public sealed class ServiceRecordFormModel
{
    public DateOnly? DateServiced { get; set; }

    public int? OdometerReading { get; set; }

    public string? ShopOrPerson { get; set; }

    public decimal? Tax { get; set; }

    public ServiceRecordRequest ToRequest() =>
        new() { DateServiced = DateServiced, OdometerReading = OdometerReading, ShopOrPerson = ShopOrPerson, Tax = Tax };

    public static ServiceRecordFormModel From(ServiceRecordDto record) =>
        new() { DateServiced = record.DateServiced, OdometerReading = record.OdometerReading, ShopOrPerson = record.ShopOrPerson, Tax = record.Tax };
}
