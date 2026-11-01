using MyGarage.Database.VehicleImages;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles.Images;

public static class VehicleImageMapper
{
    public static VehicleImageDto ToDto(VehicleImageRecord image) =>
        new()
        {
            Id = image.Id,
            Caption = image.Caption,
            IsMain = image.IsMain,
            ContentType = image.ContentType,
            SizeBytes = image.SizeBytes,
            CreatedAt = image.CreatedAt,
        };

    public static IReadOnlyList<VehicleImageDto> ToDtos(IEnumerable<VehicleImageRecord> images) => images.Select(ToDto).ToList();
}
