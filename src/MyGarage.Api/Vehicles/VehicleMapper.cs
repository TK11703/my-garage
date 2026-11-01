using MyGarage.Database.Common;
using MyGarage.Database.Vehicles;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles;

public static class VehicleMapper
{
    public static VehicleRecord ToRecord(VehicleRequest request, string ownerId, Guid id, DateTimeOffset now) =>
        new()
        {
            Id = id,
            OwnerId = ownerId,
            Nickname = request.Nickname!.Trim(),
            Make = request.Make!.Trim(),
            Model = request.Model!.Trim(),
            Trim = Clean(request.Trim),
            Color = Clean(request.Color),
            Mileage = request.Mileage,
            Doors = request.Doors,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = ownerId,
            UpdatedBy = ownerId,
        };

    public static VehicleDetailDto ToDetailDto(
        VehicleRecord vehicle,
        IReadOnlyList<VehicleTagDto> tags,
        IReadOnlyList<VehicleImageDto> images) =>
        new()
        {
            Id = vehicle.Id,
            Nickname = vehicle.Nickname,
            Make = vehicle.Make,
            Model = vehicle.Model,
            Trim = vehicle.Trim,
            Color = vehicle.Color,
            Mileage = vehicle.Mileage,
            Doors = vehicle.Doors,
            Tags = tags,
            Images = images,
            CreatedAt = vehicle.CreatedAt,
            UpdatedAt = vehicle.UpdatedAt,
        };

    public static VehicleSummaryDto ToSummaryDto(VehicleSummaryRecord vehicle) =>
        new()
        {
            Id = vehicle.Id,
            Nickname = vehicle.Nickname,
            Make = vehicle.Make,
            Model = vehicle.Model,
            Mileage = vehicle.Mileage,
            MainImageId = vehicle.MainImageId,
        };

    public static PagedResultDto<VehicleSummaryDto> ToPageDto(PagedResult<VehicleSummaryRecord> page) =>
        new()
        {
            Items = page.Items.Select(ToSummaryDto).ToList(),
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount,
            TotalPages = page.TotalPages,
        };

    // Blank optional text is stored as absent.
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static VehicleSearchCriteria ToCriteria(VehicleListQuery query) =>
        new()
        {
            Query = Clean(query.Q),
            Make = Clean(query.Make),
            Color = Clean(query.Color),
            Doors = query.Doors,
            Tag = Clean(query.Tag),
            MinMileage = query.MinMileage,
            MaxMileage = query.MaxMileage,
        };

    public static VehicleFilterOptionsDto ToFilterOptionsDto(VehicleFilterOptionsRecord options) =>
        new() { Makes = options.Makes, Colors = options.Colors, Tags = options.Tags };
}
