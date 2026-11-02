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
            TotalCost = detail.Record.TotalCost,
            CreatedAt = detail.Record.CreatedAt,
            UpdatedAt = detail.Record.UpdatedAt,
        };

    public static ServiceLineItemDto ToLineItemDto(ServiceLineItemRecord line) =>
        new()
        {
            Id = line.Id,
            Description = line.Description,
            FlatCost = line.FlatCost,
            LaborHours = line.LaborHours,
            LaborRate = line.LaborRate,
            LaborCost = line.LaborHours is { } hours && line.LaborRate is { } rate ? ServiceCostCalculator.LaborCost(hours, rate) : null,
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
            [.. (request.Parts ?? []).Select(part => new ServicePartInput(part.Name!.Trim(), part.Quantity!.Value, part.UnitPrice!.Value))]);

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
