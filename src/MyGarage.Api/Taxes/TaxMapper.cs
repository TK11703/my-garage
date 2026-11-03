using MyGarage.Database.Common;
using MyGarage.Database.Taxes;
using MyGarage.Shared.Taxes;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Taxes;

public static class TaxMapper
{
    public static string ToStoredType(TaxType type) =>
        type switch
        {
            TaxType.PersonalProperty => "personal_property",
            TaxType.Lease => "lease",
            TaxType.Purchase => "purchase",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown tax type."),
        };

    public static TaxType FromStoredType(string stored) =>
        stored switch
        {
            "personal_property" => TaxType.PersonalProperty,
            "lease" => TaxType.Lease,
            "purchase" => TaxType.Purchase,
            _ => throw new ArgumentOutOfRangeException(nameof(stored), stored, "Unknown stored tax type."),
        };

    public static string ToStoredRecurrence(TaxRecurrence recurrence) =>
        recurrence switch
        {
            TaxRecurrence.Once => "once",
            TaxRecurrence.Monthly => "monthly",
            TaxRecurrence.BiAnnual => "bi_annual",
            TaxRecurrence.Annual => "annual",
            _ => throw new ArgumentOutOfRangeException(nameof(recurrence), recurrence, "Unknown tax recurrence."),
        };

    public static TaxRecurrence FromStoredRecurrence(string stored) =>
        stored switch
        {
            "once" => TaxRecurrence.Once,
            "monthly" => TaxRecurrence.Monthly,
            "bi_annual" => TaxRecurrence.BiAnnual,
            "annual" => TaxRecurrence.Annual,
            _ => throw new ArgumentOutOfRangeException(nameof(stored), stored, "Unknown stored tax recurrence."),
        };

    public static TaxDto ToDto(TaxRecord record) =>
        new()
        {
            Id = record.Id,
            VehicleId = record.VehicleId,
            Type = FromStoredType(record.TaxType),
            Amount = record.Amount,
            Recurrence = FromStoredRecurrence(record.Recurrence),
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            FromEmail = record.FromEmail,
        };

    public static PagedResultDto<TaxDto> ToPageDto(PagedResult<TaxRecord> page) =>
        new()
        {
            Items = page.Items.Select(ToDto).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount,
            TotalPages = page.TotalPages,
        };
}
