using System.Diagnostics.Metrics;

namespace MyGarage.Api.Vehicles;

// Counters only: no vehicle details.
public static class VehicleMetrics
{
    public const string MeterName = "MyGarage.Vehicles";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Archived =
        Meter.CreateCounter<long>("vehicles_archived", description: "Vehicles an owner archived.");

    public static readonly Counter<long> Restored =
        Meter.CreateCounter<long>("vehicles_restored", description: "Archived vehicles an owner restored.");
}
