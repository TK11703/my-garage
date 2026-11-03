using MyGarage.Shared.Taxes;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Web.Tests.Taxes;

public static class TaxTestData
{
    public static TaxDto Entry(
        Guid? vehicleId = null,
        Guid? id = null,
        TaxType type = TaxType.PersonalProperty,
        decimal amount = 312.40m,
        TaxRecurrence recurrence = TaxRecurrence.Annual,
        bool fromEmail = false) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            VehicleId = vehicleId ?? Guid.NewGuid(),
            Type = type,
            Amount = amount,
            Recurrence = recurrence,
            FromEmail = fromEmail,
            CreatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero),
        };

    // Not named Page: VehicleTestContext has a protected static Page that wins over static imports.
    public static PagedResultDto<TaxDto> TaxPage(IReadOnlyList<TaxDto> items, int page = 1, int totalCount = -1, int pageSize = 10)
    {
        var total = totalCount < 0 ? items.Count : totalCount;
        return new PagedResultDto<TaxDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize),
        };
    }
}
