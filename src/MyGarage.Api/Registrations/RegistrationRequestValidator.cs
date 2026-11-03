using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Shared.Registrations;

namespace MyGarage.Api.Registrations;

public sealed class RegistrationRequestValidator : AbstractValidator<RegistrationRequest>
{
    private static readonly DateOnly EarliestDate = new(1900, 1, 1);
    private static readonly DateOnly LatestDate = new(2100, 12, 31);

    public RegistrationRequestValidator()
    {
        RuleFor(request => request.State)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("State is required.")
            .Must(UsStates.IsValid).WithMessage("Choose one of the 50 states or DC.");
        RuleFor(request => request.Cost)
            .NotNull().WithMessage("Cost is required.")
            .InclusiveBetween(0m, MoneyRules.MaxAmount).WithMessage("Cost must be between 0.00 and 9,999,999.99.")
            .Must(MoneyRules.HasAtMostTwoDecimals).WithMessage("Cost can have at most 2 decimal places.");
        RuleFor(request => request.ExpirationDate)
            .NotNull().WithMessage("Expiration date is required.")
            .Must(date => date is null || (date >= EarliestDate && date <= LatestDate))
            .WithMessage("Expiration date must be between 1900-01-01 and 2100-12-31.");
    }
}
