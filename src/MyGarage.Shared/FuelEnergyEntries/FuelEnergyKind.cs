using System.Text.Json.Serialization;

namespace MyGarage.Shared.FuelEnergyEntries;

// Fuel is priced per gallon and measured in gallons; Energy is priced per kWh and measured in kWh delivered.
[JsonConverter(typeof(JsonStringEnumConverter<FuelEnergyKind>))]
public enum FuelEnergyKind
{
    Fuel,
    Energy,
}
