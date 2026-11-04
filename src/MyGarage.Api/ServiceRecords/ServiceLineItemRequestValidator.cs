using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Database.ServiceRecords;
using MyGarage.Shared.ServiceRecords;

namespace MyGarage.Api.ServiceRecords;

public sealed class ServiceLineItemRequestValidator : AbstractValidator<ServiceLineItemRequest>
{
    public const int MaxParts = 25;

    public ServiceLineItemRequestValidator()
    {
        RuleFor(request => request.Description).TrimmedText("Description", 200, required: true);

        RuleFor(request => request.FlatCost)
            .InclusiveBetween(-MoneyRules.MaxAmount, MoneyRules.MaxAmount).WithMessage("Flat cost must be between -9,999,999.99 and 9,999,999.99.")
            .Must(MoneyRules.HasAtMostTwoDecimals).WithMessage("Flat cost can have at most two decimal places.");
        RuleFor(request => request.LaborHours)
            .InclusiveBetween(0.01m, 999.99m).WithMessage("Labor hours must be between 0.01 and 999.99.")
            .Must(MoneyRules.HasAtMostTwoDecimals).WithMessage("Labor hours can have at most two decimal places.");
        RuleFor(request => request.LaborRate)
            .InclusiveBetween(0m, 99_999.99m).WithMessage("Hourly rate must be between 0.00 and 99,999.99.")
            .Must(MoneyRules.HasAtMostTwoDecimals).WithMessage("Hourly rate can have at most two decimal places.");
        RuleFor(request => request.LaborRate)
            .NotNull().When(request => request.LaborHours is not null).WithMessage("Hourly rate is required with labor hours.");
        RuleFor(request => request.LaborHours)
            .NotNull().When(request => request.LaborRate is not null).WithMessage("Labor hours are required with an hourly rate.");
        RuleFor(request => request.LaborCost)
            .InclusiveBetween(0m, MoneyRules.MaxAmount).WithMessage("Total labor must be between 0.00 and 9,999,999.99.")
            .Must(MoneyRules.HasAtMostTwoDecimals).WithMessage("Total labor can have at most two decimal places.");

        RuleFor(request => request.Parts)
            .Must(parts => (parts?.Count ?? 0) <= MaxParts).WithMessage($"A line item can have at most {MaxParts} parts.");
        RuleForEach(request => request.Parts).SetValidator(new ServicePartRequestValidator());

        RuleFor(request => request).Custom(ValidatePricing);
    }

    private static void ValidatePricing(ServiceLineItemRequest request, ValidationContext<ServiceLineItemRequest> context)
    {
        var parts = request.Parts ?? [];
        var hasFlat = request.FlatCost is not null;
        var hasBreakdown = request.LaborHours is not null || request.LaborRate is not null || request.LaborCost is not null || parts.Count > 0;

        if (hasFlat && hasBreakdown)
        {
            context.AddFailure("Pricing", "Use either a flat cost or labor and parts, not both.");
            return;
        }

        if (!hasFlat && !hasBreakdown)
        {
            context.AddFailure("Pricing", "Enter a flat cost, labor (hours and a rate, or a total), or at least one part.");
            return;
        }

        if (!hasFlat
            && parts.Count <= MaxParts
            && parts.All(part => part.Quantity is not null && part.UnitPrice is not null)
            && ((request.LaborHours is null) == (request.LaborRate is null))
            && ServiceCostCalculator.LineTotal(
                null, request.LaborHours, request.LaborRate, request.LaborCost, parts.Select(part => (part.Quantity!.Value, part.UnitPrice!.Value))) > MoneyRules.MaxAmount)
        {
            context.AddFailure("Pricing", "The total for this line item cannot exceed 9,999,999.99.");
        }
    }
}
