namespace MyGarage.Shared.Insurance;

public sealed class InsurancePolicyRequest
{
    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    // How often the premium is paid.
    public InsuranceDuration? Duration { get; init; }

    // The premium paid each time, not the policy total.
    public decimal? Cost { get; init; }
}
