using System.Text.Json.Serialization;

namespace MyGarage.Shared.Drafts;

// How the proposed vehicle was found: by VIN, by year/make/model, because it is the owner's only vehicle, because the owner chose it when uploading, or not at all.
[JsonConverter(typeof(SnakeCaseEnumConverter<VehicleMatch>))]
public enum VehicleMatch
{
    Vin,
    Description,
    OnlyVehicle,
    None,
    Selected,
}
