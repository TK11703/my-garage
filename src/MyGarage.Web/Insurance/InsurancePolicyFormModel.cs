using MyGarage.Shared.Insurance;

namespace MyGarage.Web.Insurance;

public sealed class InsurancePolicyFormModel
{
    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public InsuranceDuration? Duration { get; set; }

    public decimal? Cost { get; set; }

    public InsurancePolicyRequest ToRequest() =>
        new() { StartDate = StartDate, EndDate = EndDate, Duration = Duration, Cost = Cost };

    public static InsurancePolicyFormModel From(InsurancePolicyDto policy) =>
        new()
        {
            StartDate = policy.StartDate,
            EndDate = policy.EndDate,
            Duration = policy.Duration,
            Cost = policy.Cost,
        };
}
