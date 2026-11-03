using MyGarage.Database.Common;
using MyGarage.Database.Inspections;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Inspections;

public static class InspectionMapper
{
    public static string ToStoredType(InspectionType type) =>
        type switch
        {
            InspectionType.Safety => "safety",
            InspectionType.Emissions => "emissions",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown inspection type."),
        };

    public static InspectionType FromStoredType(string stored) =>
        stored switch
        {
            "safety" => InspectionType.Safety,
            "emissions" => InspectionType.Emissions,
            _ => throw new ArgumentOutOfRangeException(nameof(stored), stored, "Unknown stored inspection type."),
        };

    public static InspectionDto ToDto(InspectionRecord record) =>
        new()
        {
            Id = record.Id,
            VehicleId = record.VehicleId,
            Type = FromStoredType(record.InspectionType),
            InspectionDate = record.InspectionDate,
            Cost = record.Cost,
            NextDueDate = record.NextDueDate,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
        };

    public static PagedResultDto<InspectionDto> ToPageDto(PagedResult<InspectionRecord> page) =>
        new()
        {
            Items = page.Items.Select(ToDto).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount,
            TotalPages = page.TotalPages,
        };
}
