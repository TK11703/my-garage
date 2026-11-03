using MyGarage.Shared.Inspections;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Web.Tests.Inspections;

public static class InspectionTestData
{
    public static InspectionDto Entry(
        Guid? vehicleId = null,
        Guid? id = null,
        InspectionType type = InspectionType.Safety,
        DateOnly? date = null,
        decimal cost = 25.50m,
        DateOnly? nextDueDate = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            VehicleId = vehicleId ?? Guid.NewGuid(),
            Type = type,
            InspectionDate = date ?? new DateOnly(2026, 5, 1),
            Cost = cost,
            NextDueDate = nextDueDate,
            CreatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
        };

    // Not named Page: VehicleTestContext has a protected static Page that wins over static imports.
    public static PagedResultDto<InspectionDto> InspectionPage(IReadOnlyList<InspectionDto> items, int page = 1, int totalCount = -1, int pageSize = 10)
    {
        var total = totalCount < 0 ? items.Count : totalCount;
        return new PagedResultDto<InspectionDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }
}
