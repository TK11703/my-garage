using MyGarage.Shared.Registrations;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Web.Tests.Registrations;

public static class RegistrationTestData
{
    public static RegistrationDto Entry(
        Guid? vehicleId = null,
        Guid? id = null,
        string state = "WA",
        decimal cost = 78.75m,
        DateOnly? expirationDate = null,
        bool fromEmail = false) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            VehicleId = vehicleId ?? Guid.NewGuid(),
            State = state,
            Cost = cost,
            ExpirationDate = expirationDate ?? new DateOnly(2027, 5, 1),
            FromEmail = fromEmail,
            CreatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
        };

    // Not named Page: VehicleTestContext has a protected static Page that wins over static imports.
    public static PagedResultDto<RegistrationDto> RegistrationPage(IReadOnlyList<RegistrationDto> items, int page = 1, int totalCount = -1, int pageSize = 10)
    {
        var total = totalCount < 0 ? items.Count : totalCount;
        return new PagedResultDto<RegistrationDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }
}
