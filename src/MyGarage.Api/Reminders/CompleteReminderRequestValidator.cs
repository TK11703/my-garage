using FluentValidation;
using MyGarage.Shared.Reminders;

namespace MyGarage.Api.Reminders;

// Whether a mileage is required depends on the reminder, so the endpoint checks that after loading it.
public sealed class CompleteReminderRequestValidator : AbstractValidator<CompleteReminderRequest>
{
    public CompleteReminderRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(request => request.DoneOn)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("Done on date is required.")
            .Must(date => ReminderRules.IsValidDoneDate(date!.Value, timeProvider))
            .WithMessage("Done on date must be between 1900-01-01 and today.");

        RuleFor(request => request.Mileage)
            .InclusiveBetween(0, ReminderRules.MaxMileage).When(request => request.Mileage is not null)
            .WithMessage($"Mileage must be a whole number from 0 to {ReminderRules.MaxMileage:N0}.");
    }
}
