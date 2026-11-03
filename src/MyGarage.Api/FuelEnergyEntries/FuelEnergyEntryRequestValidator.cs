using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Shared.FuelEnergyEntries;

namespace MyGarage.Api.FuelEnergyEntries;

public sealed class FuelEnergyEntryRequestValidator : AbstractValidator<FuelEnergyEntryRequest>
{
    private static readonly DateOnly EarliestDate = new(1900, 1, 1);

    public FuelEnergyEntryRequestValidator(TimeProvider timeProvider)
    {
        DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        RuleFor(request => request.Kind)
            .NotNull().WithMessage("Choose fuel or charging.")
            .IsInEnum().WithMessage("Choose fuel or charging.");
        RuleFor(request => request.EntryDate)
            .NotNull().WithMessage("Entry date is required.")
            .Must(date => date is null || date <= Today()).WithMessage("Entry date cannot be in the future.")
            .Must(date => date is null || date >= EarliestDate).WithMessage("Entry date cannot be before 1900.");
        RuleFor(request => request.UnitPrice)
            .NotNull().WithMessage("Price is required.")
            .InclusiveBetween(0m, 9_999.9999m).WithMessage("Price must be between 0 and 9,999.9999.")
            .Must(value => MoneyRules.HasAtMostDecimals(value, 4)).WithMessage("Price can have at most 4 decimal places.");
        RuleFor(request => request.Quantity)
            .NotNull().WithMessage("Amount is required.")
            .InclusiveBetween(0.001m, 99_999.999m).WithMessage("Amount must be between 0.001 and 99,999.999.")
            .Must(value => MoneyRules.HasAtMostDecimals(value, 3)).WithMessage("Amount can have at most 3 decimal places.");
        RuleFor(request => request.TotalCost)
            .InclusiveBetween(0m, MoneyRules.MaxAmount).WithMessage("Total must be between 0.00 and 9,999,999.99.")
            .Must(MoneyRules.HasAtMostTwoDecimals).WithMessage("Total can have at most 2 decimal places.");

        // A calculated total must also fit the stored range.
        RuleFor(request => request.TotalCost)
            .Must((request, _) => FuelEnergyTotal.Resolve(request.UnitPrice!.Value, request.Quantity!.Value, null) <= MoneyRules.MaxAmount)
            .When(request => request.TotalCost is null
                             && request.UnitPrice is >= 0m and <= 9_999.9999m
                             && request.Quantity is >= 0.001m and <= 99_999.999m)
            .WithMessage("Total is too large.");
    }
}
