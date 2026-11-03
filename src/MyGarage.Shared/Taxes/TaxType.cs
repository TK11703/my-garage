using System.Text.Json.Serialization;

namespace MyGarage.Shared.Taxes;

// What the tax is charged for.
[JsonConverter(typeof(JsonStringEnumConverter<TaxType>))]
public enum TaxType
{
    PersonalProperty,
    Lease,
    Purchase,
}
