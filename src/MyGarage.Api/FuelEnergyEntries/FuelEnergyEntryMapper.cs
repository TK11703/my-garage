using MyGarage.Database.Common;
using MyGarage.Database.FuelEnergyEntries;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.FuelEnergyEntries;

public static class FuelEnergyEntryMapper
{
    public static string ToStoredKind(FuelEnergyKind kind) =>
        kind switch
        {
            FuelEnergyKind.Fuel => "fuel",
            FuelEnergyKind.Energy => "energy",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind."),
        };

    public static FuelEnergyKind FromStoredKind(string stored) =>
        stored switch
        {
            "fuel" => FuelEnergyKind.Fuel,
            "energy" => FuelEnergyKind.Energy,
            _ => throw new ArgumentOutOfRangeException(nameof(stored), stored, "Unknown stored kind."),
        };

    public static FuelEnergyEntryDto ToDto(FuelEnergyEntryRecord record) =>
        new()
        {
            Id = record.Id,
            VehicleId = record.VehicleId,
            Kind = FromStoredKind(record.Kind),
            EntryDate = record.EntryDate,
            UnitPrice = record.UnitPrice,
            Quantity = record.Quantity,
            TotalCost = record.TotalCost,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
        };

    public static PagedResultDto<FuelEnergyEntryDto> ToPageDto(PagedResult<FuelEnergyEntryRecord> page) =>
        new()
        {
            Items = page.Items.Select(ToDto).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount,
            TotalPages = page.TotalPages,
        };
}
