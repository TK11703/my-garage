using MyGarage.Shared.Insurance;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Web.Tests.Insurance;

public static class InsurancePolicyTestData
{
    public static InsurancePolicyDto Entry(
        Guid? vehicleId = null,
        Guid? id = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        InsuranceDuration duration = InsuranceDuration.Annual,
        decimal cost = 1240m) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            VehicleId = vehicleId ?? Guid.NewGuid(),
            StartDate = startDate ?? new DateOnly(2026, 5, 1),
            EndDate = endDate ?? new DateOnly(2027, 5, 1),
            Duration = duration,
            Cost = cost,
            CreatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
        };

    // Not named Page: VehicleTestContext has a protected static Page that wins over static imports.
    public static PagedResultDto<InsurancePolicyDto> InsurancePage(IReadOnlyList<InsurancePolicyDto> items, int page = 1, int totalCount = -1, int pageSize = 10)
    {
        var total = totalCount < 0 ? items.Count : totalCount;
        return new PagedResultDto<InsurancePolicyDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }
}
