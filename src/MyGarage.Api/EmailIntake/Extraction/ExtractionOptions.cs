namespace MyGarage.Api.EmailIntake.Extraction;

// Bound from EmailIntake:Extraction. With no endpoint or deployment, extraction is disabled (the app still works, attachments become manual-entry drafts).
public sealed class ExtractionOptions
{
    public const string SectionName = "EmailIntake:Extraction";

    public string? Endpoint { get; set; }

    public string? Deployment { get; set; }

    // Upper bound for one extraction call, including resilience retries.
    public int TimeoutSeconds { get; set; } = 90;
}
