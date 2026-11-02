using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Shared.ServiceRecords;

namespace MyGarage.Api.ServiceRecords;

public sealed class ServicePartRequestValidator : AbstractValidator<ServicePartRequest>
{
    public ServicePartRequestValidator()
    {
        RuleFor(part => part.Name).TrimmedText("Part name", 100, required: true);
        RuleFor(part => part.Quantity)
            .NotNull().WithMessage("Quantity is required.")
            .InclusiveBetween(0.01m, 9999.99m).WithMessage("Quantity must be between 0.01 and 9,999.99.")
            .Must(MoneyRules.HasAtMostTwoDecimals).WithMessage("Quantity can have at most two decimal places.");
        RuleFor(part => part.UnitPrice)
            .NotNull().WithMessage("Price per part is required.")
            .InclusiveBetween(0m, MoneyRules.MaxAmount).WithMessage("Price per part must be between 0.00 and 9,999,999.99.")
            .Must(MoneyRules.HasAtMostTwoDecimals).WithMessage("Price per part can have at most two decimal places.");
    }
}
