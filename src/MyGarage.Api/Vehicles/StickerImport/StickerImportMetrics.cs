using System.Diagnostics.Metrics;

namespace MyGarage.Api.Vehicles.StickerImport;

// Counters only: outcomes and durations, never anything read from a sticker. Rate-limited calls are counted by the ASP.NET Core rate limiting meter.
public static class StickerImportMetrics
{
    public const string MeterName = "MyGarage.StickerImport";

    private static readonly Meter Meter = new(MeterName);

    // Tagged outcome = filled, partial, no_details, unavailable (the reader failed or is off), or refused (no file, bad file, not the caller's vehicle).
    public static readonly Counter<long> Extractions =
        Meter.CreateCounter<long>("sticker_extractions", description: "Window sticker extraction requests by outcome.");

    // Tagged outcome as above.
    public static readonly Histogram<double> ExtractionDuration =
        Meter.CreateHistogram<double>("sticker_extraction_duration", unit: "ms", description: "Time spent handling one window sticker extraction request.");

    public static void Record(string outcome, TimeSpan elapsed)
    {
        var tag = new KeyValuePair<string, object?>("outcome", outcome);
        Extractions.Add(1, tag);
        ExtractionDuration.Record(elapsed.TotalMilliseconds, tag);
    }
}
