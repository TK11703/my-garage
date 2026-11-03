namespace MyGarage.Api.EmailIntake;

// Bound from EmailIntake:Processing. Tests turn the background loop off and call SubmissionProcessor.ProcessOnceAsync themselves.
public sealed class SubmissionProcessingOptions
{
    public const string SectionName = "EmailIntake:Processing";

    // False stops the polling loop from running at all.
    public bool Enabled { get; set; } = true;

    // How long the loop waits after finding nothing to do.
    public int PollIntervalSeconds { get; set; } = 5;

    // How long a claimed submission stays leased to one worker before another may take it over.
    public int LeaseMinutes { get; set; } = 10;
}
