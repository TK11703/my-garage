using System.Text.Json.Serialization;

namespace MyGarage.Shared.Vehicles;

[JsonConverter(typeof(JsonStringEnumConverter<OwnershipType>))]
public enum OwnershipType
{
    Purchased,
    Leased,
}
