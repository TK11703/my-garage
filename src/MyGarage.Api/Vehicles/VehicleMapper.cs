using MyGarage.Database.Common;
using MyGarage.Database.Lookups;
using MyGarage.Database.Vehicles;
using MyGarage.Shared.Lookups;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles;

public static class VehicleMapper
{
    public static VehicleRecord ToRecord(VehicleRequest request, string ownerId, Guid id, DateTimeOffset now) =>
        new()
        {
            Id = id,
            OwnerId = ownerId,
            Vin = Clean(request.Vin)?.ToUpperInvariant(),
            Make = request.Make!.Trim(),
            Model = request.Model!.Trim(),
            Trim = Clean(request.Trim),
            Year = request.Year,
            VehicleTypeId = request.VehicleTypeId,
            Ownership = request.Ownership switch
            {
                OwnershipType.Purchased => "purchased",
                OwnershipType.Leased => "leased",
                _ => null,
            },
            PurchasedOn = request.PurchasedOn,
            LeaseEndsOn = request.LeaseEndsOn,
            Engine = Clean(request.Engine),
            Horsepower = request.Horsepower,
            FuelTypeId = request.FuelTypeId,
            TransmissionId = request.TransmissionId,
            Mileage = request.Mileage,
            LatestOdometerReading = request.LatestOdometerReading,
            Color = Clean(request.Color),
            Seats = request.Seats,
            Doors = request.Doors,
            DrivetrainId = request.DrivetrainId,
            WheelSize = request.WheelSize,
            TireSize = Clean(request.TireSize),
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
            Vin = vehicle.Vin,
            Make = vehicle.Make,
            Model = vehicle.Model,
            Trim = vehicle.Trim,
            Year = vehicle.Year,
            VehicleType = Value(vehicle.VehicleTypeId, vehicle.VehicleTypeName),
            Ownership = ToOwnership(vehicle.Ownership),
            PurchasedOn = vehicle.PurchasedOn,
            LeaseEndsOn = vehicle.LeaseEndsOn,
            Engine = vehicle.Engine,
            Horsepower = vehicle.Horsepower,
            FuelType = Value(vehicle.FuelTypeId, vehicle.FuelTypeName),
            Transmission = Value(vehicle.TransmissionId, vehicle.TransmissionName),
            Mileage = vehicle.Mileage,
            LatestOdometerReading = vehicle.LatestOdometerReading,
            Color = vehicle.Color,
            Seats = vehicle.Seats,
            Doors = vehicle.Doors,
            Drivetrain = Value(vehicle.DrivetrainId, vehicle.DrivetrainName),
            WheelSize = vehicle.WheelSize,
            TireSize = vehicle.TireSize,
            Tags = tags,
            Images = images,
            CreatedAt = vehicle.CreatedAt,
            UpdatedAt = vehicle.UpdatedAt,
        };

    private static OwnershipType? ToOwnership(string? stored) =>
        stored switch
        {
            "purchased" => OwnershipType.Purchased,
            "leased" => OwnershipType.Leased,
            _ => null,
        };

    private static LookupValueDto? Value(Guid? id, string? name) =>
        id is { } itemId && name is not null ? new LookupValueDto { Id = itemId, Name = name } : null;

    public static VehicleSummaryDto ToSummaryDto(VehicleSummaryRecord vehicle) =>
        new()
        {
            Id = vehicle.Id,
            Year = vehicle.Year,
            Make = vehicle.Make,
            Model = vehicle.Model,
            Mileage = vehicle.Mileage,
            MainImageId = vehicle.MainImageId,
            FuelType = vehicle.FuelTypeName,
            Transmission = vehicle.TransmissionName,
            Ownership = ToOwnership(vehicle.Ownership),
            PurchasedYear = vehicle.PurchasedOn?.Year,
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
            MinYear = query.MinYear,
            MaxYear = query.MaxYear,
            VehicleTypeId = query.VehicleTypeId,
            FuelTypeId = query.FuelTypeId,
            TransmissionId = query.TransmissionId,
            DrivetrainId = query.DrivetrainId,
        };

    public static VehicleFilterOptionsDto ToFilterOptionsDto(VehicleFilterOptionsRecord options) =>
        new()
        {
            Makes = options.Makes,
            Colors = options.Colors,
            Tags = options.Tags,
            VehicleTypes = [.. options.VehicleTypes.Select(Option)],
            FuelTypes = [.. options.FuelTypes.Select(Option)],
            Transmissions = [.. options.Transmissions.Select(Option)],
            Drivetrains = [.. options.Drivetrains.Select(Option)],
        };

    private static LookupValueDto Option(LookupOptionRecord option) => new() { Id = option.Id, Name = option.Name };
}
