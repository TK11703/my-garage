using System.Globalization;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Web.Common;

namespace MyGarage.Web.ServiceRecords;

public static class ServiceRecordFormat
{
    public static string Odometer(int reading) => $"{reading.ToString("N0", CultureInfo.InvariantCulture)} mi";

    public static string LineItems(int count) => count == 1 ? "1 line item" : $"{count.ToString(CultureInfo.InvariantCulture)} line items";

    public static string ShopOrNotSet(string? shop) => string.IsNullOrWhiteSpace(shop) ? DisplayFormat.NotSet : shop;

    public static string RecordCount(int count) => count == 1 ? "1 service record" : $"{count.ToString(CultureInfo.InvariantCulture)} service records";

    public static string LaborLine(ServiceLineItemDto line) =>
        line.LaborHours is null
            ? $"Labor: {DisplayFormat.Money(line.LaborCost ?? 0)}"
            : $"Labor: {DisplayFormat.Number(line.LaborHours.Value)} h x {DisplayFormat.Money(line.LaborRate ?? 0)} = {DisplayFormat.Money(line.LaborCost ?? 0)}";

    public static string PartLine(ServicePartDto part) =>
        $"{DisplayFormat.Number(part.Quantity)} x {part.Name} at {DisplayFormat.Money(part.UnitPrice)} = {DisplayFormat.Money(part.TotalPrice)}";
}
