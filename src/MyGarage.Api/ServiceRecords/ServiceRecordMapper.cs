using MyGarage.Database.Common;
using MyGarage.Database.ServiceRecords;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.ServiceRecords;

public static class ServiceRecordMapper
{
    public static ServiceRecordSummaryDto ToSummaryDto(ServiceRecordRecord record) =>
        new()
        {
            Id = record.Id,
            DateServiced = record.DateServiced,
            OdometerReading = record.OdometerReading,
            ShopOrPerson = record.ShopOrPerson,
            LineItemCount = record.LineItemCount,
            TotalCost = record.TotalCost,
            FromEmail = record.FromEmail,
        };

    public static ServiceRecordDto ToDto(ServiceRecordDetailRecord detail) =>
        new()
        {
            Id = detail.Record.Id,
            VehicleId = detail.Record.VehicleId,
            DateServiced = detail.Record.DateServiced,
            OdometerReading = detail.Record.OdometerReading,
            ShopOrPerson = detail.Record.ShopOrPerson,
            LineItems = [.. detail.LineItems.Select(ToLineItemDto)],
            Tax = detail.Record.TaxAmount,
            TotalCost = detail.Record.TotalCost,
            CreatedAt = detail.Record.CreatedAt,
            UpdatedAt = detail.Record.UpdatedAt,
            FromEmail = detail.Record.FromEmail,
        };

    public static ServiceLineItemDto ToLineItemDto(ServiceLineItemRecord line) =>
        new()
        {
            Id = line.Id,
            Description = line.Description,
            FlatCost = line.FlatCost,
            LaborHours = line.LaborHours,
            LaborRate = line.LaborRate,
            LaborCost = ServiceCostCalculator.EffectiveLaborCost(line.LaborHours, line.LaborRate, line.LaborCost),
            Parts = [.. line.Parts.Select(part => new ServicePartDto
            {
                Name = part.Name,
                Quantity = part.Quantity,
                UnitPrice = part.UnitPrice,
                TotalPrice = ServiceCostCalculator.PartTotal(part.Quantity, part.UnitPrice),
            })],
            TotalCost = line.TotalCost,
        };

    // The validator has already guaranteed the values are present and consistent.
    public static ServiceLineItemInput ToInput(ServiceLineItemRequest request) =>
        new(
            request.Description!.Trim(),
            request.FlatCost,
            request.LaborHours,
            request.LaborRate,
            [.. (request.Parts ?? []).Select(part => new ServicePartInput(part.Name!.Trim(), part.Quantity!.Value, part.UnitPrice!.Value))],
            request.LaborCost);

    // Empty and zero both mean no tax.
    public static decimal? CleanTax(decimal? tax) => tax is null or 0m ? null : tax;

    public static PagedResultDto<ServiceRecordSummaryDto> ToPageDto(PagedResult<ServiceRecordRecord> page) =>
        new()
        {
            Items = page.Items.Select(ToSummaryDto).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount,
            TotalPages = page.TotalPages,
        };

    // Free text: trimmed, and blank means not set.
    public static string? CleanShop(string? shop) => string.IsNullOrWhiteSpace(shop) ? null : shop.Trim();
}
