using System.Text.Json.Serialization;

namespace MyGarage.Shared.Insurance;

// How often the premium is paid.
[JsonConverter(typeof(JsonStringEnumConverter<InsuranceDuration>))]
public enum InsuranceDuration
{
    Monthly,
    BiAnnual,
    Annual,
}
