using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Shared.Insurance;

namespace MyGarage.Api.Insurance;

public sealed class InsurancePolicyRequestValidator : AbstractValidator<InsurancePolicyRequest>
{
    private static readonly DateOnly EarliestDate = new(1900, 1, 1);
    private static readonly DateOnly LatestDate = new(2100, 12, 31);

    public InsurancePolicyRequestValidator()
    {
        RuleFor(request => request.StartDate)
            .NotNull().WithMessage("Start date is required.")
            .Must(date => date is null || (date >= EarliestDate && date <= LatestDate))
            .WithMessage("Start date must be between 1900-01-01 and 2100-12-31.");
        RuleFor(request => request.EndDate)
            .NotNull().WithMessage("End date is required.")
            .Must(date => date is null || (date >= EarliestDate && date <= LatestDate))
            .WithMessage("End date must be between 1900-01-01 and 2100-12-31.");
        RuleFor(request => request.EndDate)
            .Must((request, endDate) => endDate!.Value > request.StartDate!.Value)
            .When(request => request.StartDate is not null && request.EndDate is not null)
            .WithMessage("End date must be later than the start date.");
        // Duration is how often the premium is paid; it is not cross-checked against the dates.
        RuleFor(request => request.Duration)
            .NotNull().WithMessage("Choose how often the premium is paid.")
            .IsInEnum().WithMessage("Choose how often the premium is paid.");
        RuleFor(request => request.Cost)
            .NotNull().WithMessage("Cost is required.")
            .InclusiveBetween(0m, MoneyRules.MaxAmount).WithMessage("Cost must be between 0.00 and 9,999,999.99.")
            .Must(MoneyRules.HasAtMostTwoDecimals).WithMessage("Cost can have at most 2 decimal places.");
    }
}
