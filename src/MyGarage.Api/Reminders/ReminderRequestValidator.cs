using FluentValidation;
using MyGarage.Shared.Reminders;

namespace MyGarage.Api.Reminders;

public sealed class ReminderRequestValidator : AbstractValidator<ReminderRequest>
{
    public ReminderRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(request => request.Name)
            .Cascade(CascadeMode.Stop)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Name is required.")
            .MaximumLength(ReminderRules.MaxNameLength).WithMessage($"Name can have at most {ReminderRules.MaxNameLength} characters.");

        RuleFor(request => request.AppliesTo)
            .NotNull().WithMessage("Choose whether the reminder applies to the vehicle, equipment, or a location.");

        When(request => request.AppliesTo is ReminderAppliesTo.Equipment or ReminderAppliesTo.Location, () =>
            RuleFor(request => request.Subject)
                .Cascade(CascadeMode.Stop)
                .Must(subject => !string.IsNullOrWhiteSpace(subject))
                .WithMessage(request => $"Name the {(request.AppliesTo == ReminderAppliesTo.Equipment ? "equipment" : "location")} this reminder is about.")
                .MaximumLength(ReminderRules.MaxSubjectLength).WithMessage($"The name can have at most {ReminderRules.MaxSubjectLength} characters."));

        When(request => request.AppliesTo == ReminderAppliesTo.Vehicle, () =>
            RuleFor(request => request.Subject)
                .Must(subject => string.IsNullOrWhiteSpace(subject))
                .WithMessage("A reminder for the vehicle does not take an equipment or location name."));

        RuleFor(request => request.IntervalMiles)
            .InclusiveBetween(1, ReminderRules.MaxIntervalMiles).When(request => request.IntervalMiles is not null)
            .WithMessage($"Miles must be a whole number from 1 to {ReminderRules.MaxIntervalMiles:N0}.");

        RuleFor(request => request.IntervalMiles)
            .NotNull().When(request => request.IntervalTime is null)
            .WithMessage("Set how often it repeats: every number of miles, every number of months or years, or both.");

        When(request => request.IntervalTime is not null, () =>
            RuleFor(request => request.IntervalTime!.Value)
                .Must((request, value) => ReminderRules.TimeValueInRange(request.IntervalTime!.Unit, value))
                .WithMessage(request => request.IntervalTime!.Unit == ReminderTimeUnit.Years
                    ? $"Years must be a whole number from 1 to {ReminderRules.MaxIntervalYears}."
                    : $"Months must be a whole number from 1 to {ReminderRules.MaxIntervalMonths}."));

        RuleFor(request => request.LastDoneOn)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Last done date is required.")
            .Must(date => ReminderRules.IsValidDoneDate(date!.Value, timeProvider))
            .WithMessage("Last done date must be between 1900-01-01 and today.");

        RuleFor(request => request.LastDoneMileage)
            .InclusiveBetween(0, ReminderRules.MaxMileage).When(request => request.LastDoneMileage is not null)
            .WithMessage($"Last done mileage must be a whole number from 0 to {ReminderRules.MaxMileage:N0}.");

        RuleFor(request => request.LastDoneMileage)
            .NotNull().When(request => request.IntervalMiles is not null)
            .WithMessage("Last done mileage is required for a reminder that repeats by miles.");
    }
}
