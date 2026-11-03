using System.Text.Json.Serialization;

namespace MyGarage.Shared.Inspections;

[JsonConverter(typeof(JsonStringEnumConverter<InspectionType>))]
public enum InspectionType
{
    Safety,
    Emissions,
}
