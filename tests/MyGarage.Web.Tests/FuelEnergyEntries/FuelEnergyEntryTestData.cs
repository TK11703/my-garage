using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Web.Tests.FuelEnergyEntries;

public static class FuelEnergyEntryTestData
{
    public static FuelEnergyEntryDto Entry(
        Guid? vehicleId = null,
        Guid? id = null,
        FuelEnergyKind kind = FuelEnergyKind.Fuel,
        DateOnly? date = null,
        decimal unitPrice = 3.459m,
        decimal quantity = 12.345m,
        decimal totalCost = 42.70m) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            VehicleId = vehicleId ?? Guid.NewGuid(),
            Kind = kind,
            EntryDate = date ?? new DateOnly(2026, 5, 1),
            UnitPrice = unitPrice,
            Quantity = quantity,
            TotalCost = totalCost,
            CreatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
        };

    public static PagedResultDto<FuelEnergyEntryDto> FuelPage(IReadOnlyList<FuelEnergyEntryDto> items, int page = 1, int totalCount = -1, int pageSize = 10)
    {
        var total = totalCount < 0 ? items.Count : totalCount;
        return new PagedResultDto<FuelEnergyEntryDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }
}
