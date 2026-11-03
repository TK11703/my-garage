using System.Diagnostics.Metrics;

namespace MyGarage.Api.EmailIntake;

// Counters only: outcomes and durations, never email content.
public static class EmailIntakeMetrics
{
    public const string MeterName = "MyGarage.EmailIntake";

    private static readonly Meter Meter = new(MeterName);

    // Tagged outcome = ready, not_applicable, retry, or failed.
    public static readonly Counter<long> SubmissionsProcessed =
        Meter.CreateCounter<long>("email_submissions_processed", description: "Email submissions the processor finished an attempt on.");

    // Tagged outcome = ok or the failure reason code.
    public static readonly Histogram<double> ExtractionDuration =
        Meter.CreateHistogram<double>("email_extraction_duration", unit: "ms", description: "Time spent in one AI extraction call.");

    // Tagged decision = included, discarded, or expired.
    public static readonly Counter<long> DraftsDecided =
        Meter.CreateCounter<long>("email_drafts_decided", description: "Drafts that reached a final state.");
}
