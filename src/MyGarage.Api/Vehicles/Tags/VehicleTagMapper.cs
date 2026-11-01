using MyGarage.Database.Tags;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles.Tags;

public static class VehicleTagMapper
{
    public static VehicleTagDto ToDto(VehicleTagRecord tag) => new() { Id = tag.Id, Name = tag.Name };

    public static IReadOnlyList<VehicleTagDto> ToDtos(IEnumerable<VehicleTagRecord> tags) => tags.Select(ToDto).ToList();
}
