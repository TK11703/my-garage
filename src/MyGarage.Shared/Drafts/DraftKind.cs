using System.Text.Json.Serialization;

namespace MyGarage.Shared.Drafts;

// The record type a draft proposes; JSON and database text are service_record, fuel, energy, inspection, registration, insurance, tax.
[JsonConverter(typeof(SnakeCaseEnumConverter<DraftKind>))]
public enum DraftKind
{
    ServiceRecord,
    Fuel,
    Energy,
    Inspection,
    Registration,
    Insurance,
    Tax,
}
