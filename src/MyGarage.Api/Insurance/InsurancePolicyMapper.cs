using MyGarage.Database.Common;
using MyGarage.Database.Insurance;
using MyGarage.Shared.Insurance;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Insurance;

public static class InsurancePolicyMapper
{
    public static string ToStoredDuration(InsuranceDuration duration) =>
        duration switch
        {
            InsuranceDuration.Monthly => "monthly",
            InsuranceDuration.BiAnnual => "bi_annual",
            InsuranceDuration.Annual => "annual",
            _ => throw new ArgumentOutOfRangeException(nameof(duration), duration, "Unknown insurance duration."),
        };

    public static InsuranceDuration FromStoredDuration(string stored) =>
        stored switch
        {
            "monthly" => InsuranceDuration.Monthly,
            "bi_annual" => InsuranceDuration.BiAnnual,
            "annual" => InsuranceDuration.Annual,
            _ => throw new ArgumentOutOfRangeException(nameof(stored), stored, "Unknown stored insurance duration."),
        };

    public static InsurancePolicyDto ToDto(InsurancePolicyRecord record) =>
        new()
        {
            Id = record.Id,
            VehicleId = record.VehicleId,
            StartDate = record.StartDate,
            EndDate = record.EndDate,
            Duration = FromStoredDuration(record.Duration),
            Cost = record.Cost,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            FromEmail = record.FromEmail,
        };

    public static PagedResultDto<InsurancePolicyDto> ToPageDto(PagedResult<InsurancePolicyRecord> page) =>
        new()
        {
            Items = page.Items.Select(ToDto).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount,
            TotalPages = page.TotalPages,
        };
}
