using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Shared.ServiceRecords;

namespace MyGarage.Api.ServiceRecords;

public sealed class ServiceRecordRequestValidator : AbstractValidator<ServiceRecordRequest>
{
    private static readonly DateOnly EarliestDate = new(1900, 1, 1);

    public ServiceRecordRequestValidator(TimeProvider timeProvider)
    {
        DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        RuleFor(request => request.DateServiced)
            .NotNull().WithMessage("Date serviced is required.")
            .Must(date => date is null || date <= Today()).WithMessage("Date serviced cannot be in the future.")
            .Must(date => date is null || date >= EarliestDate).WithMessage("Date serviced cannot be before 1900.");
        RuleFor(request => request.OdometerReading)
            .NotNull().WithMessage("Odometer reading is required.")
            .InclusiveBetween(0, 9_999_999).WithMessage("Odometer reading must be between 0 and 9,999,999.");
        RuleFor(request => request.ShopOrPerson).TrimmedText("Shop name or person", 100, required: false);
        RuleFor(request => request.Tax)
            .InclusiveBetween(0m, MoneyRules.MaxAmount).WithMessage("Tax must be between 0.00 and 9,999,999.99.")
            .Must(MoneyRules.HasAtMostTwoDecimals).WithMessage("Tax can have at most two decimal places.");
    }
}
