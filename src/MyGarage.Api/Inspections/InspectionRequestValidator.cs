using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Shared.Inspections;

namespace MyGarage.Api.Inspections;

public sealed class InspectionRequestValidator : AbstractValidator<InspectionRequest>
{
    private static readonly DateOnly EarliestDate = new(1900, 1, 1);
    private static readonly DateOnly LatestNextDueDate = new(2100, 12, 31);

    public InspectionRequestValidator(TimeProvider timeProvider)
    {
        DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        RuleFor(request => request.Type)
            .NotNull().WithMessage("Choose a type.")
            .IsInEnum().WithMessage("Choose a type.");
        RuleFor(request => request.InspectionDate)
            .NotNull().WithMessage("Inspection date is required.")
            .Must(date => date is null || date <= Today()).WithMessage("Inspection date cannot be in the future.")
            .Must(date => date is null || date >= EarliestDate).WithMessage("Inspection date cannot be before 1900.");
        RuleFor(request => request.Cost)
            .NotNull().WithMessage("Cost is required.")
            .InclusiveBetween(0m, MoneyRules.MaxAmount).WithMessage("Cost must be between 0.00 and 9,999,999.99.")
            .Must(MoneyRules.HasAtMostTwoDecimals).WithMessage("Cost can have at most 2 decimal places.");
        RuleFor(request => request.NextDueDate)
            .Must((request, date) => date is null || request.InspectionDate is null || date >= request.InspectionDate)
            .WithMessage("Next due date cannot be before the inspection date.")
            .Must(date => date is null || date <= LatestNextDueDate)
            .WithMessage("Next due date cannot be after 2100-12-31.");
    }
}
