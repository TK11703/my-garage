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

    // One slice of where a record's money went; Key picks the colour.
    public sealed record SpendShare(string Key, string Label, decimal Amount);

    // Labor, parts, flat charges and tax, then any credit; categories with nothing in them are left out.
    public static IReadOnlyList<SpendShare> Breakdown(ServiceRecordDto record)
    {
        var labor = record.LineItems.Sum(line => line.LaborCost ?? 0m);
        var parts = record.LineItems.Sum(line => line.Parts.Sum(part => part.TotalPrice));
        var flat = record.LineItems.Sum(line => Math.Max(line.FlatCost ?? 0m, 0m));
        var credit = record.LineItems.Sum(line => Math.Min(line.FlatCost ?? 0m, 0m));

        return
        [
            .. new[]
            {
                new SpendShare("labor", "Labor", labor),
                new SpendShare("parts", "Parts", parts),
                new SpendShare("flat", "Flat", flat),
                new SpendShare("tax", "Tax", record.Tax ?? 0m),
                new SpendShare("credit", "Credit", credit),
            }.Where(share => share.Amount != 0m),
        ];
    }

    public sealed record LineKindInfo(string Key, string Label);

    // How a line was priced, for its pill: a credit, flat, labor only, parts only, or a mix.
    public static LineKindInfo LineKind(ServiceLineItemDto line)
    {
        var hasFlat = line.FlatCost is > 0;
        var hasLabor = line.LaborCost is > 0 || line.LaborHours is > 0;
        var hasParts = line.Parts.Count > 0;

        return (hasFlat, hasLabor, hasParts) switch
        {
            _ when line.TotalCost < 0 => new("credit", "Credit"),
            (true, false, false) => new("flat", "Flat"),
            (false, true, false) => new("labor", "Labor"),
            (false, false, true) => new("parts", "Parts"),
            _ => new("itemized", "Itemized"),
        };
    }
}
