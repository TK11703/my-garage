using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Shared.Taxes;

namespace MyGarage.Api.Taxes;

public sealed class TaxRequestValidator : AbstractValidator<TaxRequest>
{
    public TaxRequestValidator()
    {
        RuleFor(request => request.Type)
            .NotNull().WithMessage("Choose a tax type.")
            .IsInEnum().WithMessage("Choose a tax type.");
        RuleFor(request => request.Amount)
            .NotNull().WithMessage("Amount is required.")
            .InclusiveBetween(0.01m, MoneyRules.MaxAmount).WithMessage("Amount must be between 0.01 and 9,999,999.99.")
            .Must(MoneyRules.HasAtMostTwoDecimals).WithMessage("Amount can have at most 2 decimal places.");
        RuleFor(request => request.Recurrence)
            .NotNull().WithMessage("Choose how often the tax recurs.")
            .IsInEnum().WithMessage("Choose how often the tax recurs.");
    }
}
