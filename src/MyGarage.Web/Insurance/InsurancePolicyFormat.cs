using MyGarage.Shared.Insurance;
using MyGarage.Web.Common;

namespace MyGarage.Web.Insurance;

public static class InsurancePolicyFormat
{
    public static string DurationLabel(InsuranceDuration duration) =>
        duration switch
        {
            InsuranceDuration.Monthly => "Monthly",
            InsuranceDuration.BiAnnual => "Bi-Annual",
            InsuranceDuration.Annual => "Annual",
            _ => throw new ArgumentOutOfRangeException(nameof(duration), duration, "Unknown insurance duration."),
        };

    // "$120.00 Monthly": the premium paid each time and how often.
    public static string CostWithFrequency(decimal cost, InsuranceDuration duration) => $"{DisplayFormat.Money(cost)} {DurationLabel(duration)}";

    // "May 1, 2026 to May 1, 2027".
    public static string Span(DateOnly startDate, DateOnly endDate) => $"{DisplayFormat.Date(startDate)} to {DisplayFormat.Date(endDate)}";

    public static string EditName(InsurancePolicyDto policy) => $"Edit insurance ending {DisplayFormat.Date(policy.EndDate)}";
}
