using System.Text.Json.Serialization;

namespace MyGarage.Shared.Vehicles;

[JsonConverter(typeof(JsonStringEnumConverter<VehicleTimelineKind>))]
public enum VehicleTimelineKind
{
    Service,
    FuelEnergy,
    Inspection,
    Registration,
    Insurance,
    Tax,
    Journal,
}
