using System.Text.Json.Serialization;

namespace MyGarage.Shared.Taxes;

// How often the tax is paid.
[JsonConverter(typeof(JsonStringEnumConverter<TaxRecurrence>))]
public enum TaxRecurrence
{
    Once,
    Monthly,
    BiAnnual,
    Annual,
}
