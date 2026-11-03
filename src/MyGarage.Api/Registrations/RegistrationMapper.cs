using MyGarage.Database.Common;
using MyGarage.Database.Registrations;
using MyGarage.Shared.Registrations;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Registrations;

public static class RegistrationMapper
{
    public static RegistrationDto ToDto(RegistrationRecord record) =>
        new()
        {
            Id = record.Id,
            VehicleId = record.VehicleId,
            State = record.State,
            Cost = record.Cost,
            ExpirationDate = record.ExpirationDate,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
        };

    public static PagedResultDto<RegistrationDto> ToPageDto(PagedResult<RegistrationRecord> page) =>
        new()
        {
            Items = page.Items.Select(ToDto).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount,
            TotalPages = page.TotalPages,
        };
}
