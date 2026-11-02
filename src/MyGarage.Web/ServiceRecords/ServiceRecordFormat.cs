using System.Globalization;
using MyGarage.Shared.ServiceRecords;

namespace MyGarage.Web.ServiceRecords;

public static class ServiceRecordFormat
{
    public const string NotSet = "Not set";

    // One currency, shown with a dollar sign regardless of the server culture.
    public static string Money(decimal amount) =>
        (amount < 0 ? "-$" : "$") + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);

    public static string Odometer(int reading) => $"{reading.ToString("N0", CultureInfo.InvariantCulture)} mi";

    public static string Date(DateOnly date) => date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    public static string LineItems(int count) => count == 1 ? "1 line item" : $"{count.ToString(CultureInfo.InvariantCulture)} line items";

    public static string ShopOrNotSet(string? shop) => string.IsNullOrWhiteSpace(shop) ? NotSet : shop;

    public static string RecordCount(int count) => count == 1 ? "1 service record" : $"{count.ToString(CultureInfo.InvariantCulture)} service records";

    public static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string LaborLine(ServiceLineItemDto line) =>
        $"Labor: {Number(line.LaborHours ?? 0)} h x {Money(line.LaborRate ?? 0)} = {Money(line.LaborCost ?? 0)}";

    public static string PartLine(ServicePartDto part) =>
        $"{Number(part.Quantity)} x {part.Name} at {Money(part.UnitPrice)} = {Money(part.TotalPrice)}";
}
