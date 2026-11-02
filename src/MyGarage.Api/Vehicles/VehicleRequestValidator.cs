using System.Text.RegularExpressions;
using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles;

public sealed partial class VehicleRequestValidator : AbstractValidator<VehicleRequest>
{
    public VehicleRequestValidator(TimeProvider timeProvider)
    {
        DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        RuleFor(request => request.Vin)
            .Must(vin => string.IsNullOrWhiteSpace(vin) || VinPattern().IsMatch(vin.Trim().ToUpperInvariant()))
            .WithMessage("VIN must be 17 letters and digits and cannot contain I, O, or Q.");
        RuleFor(request => request.Make).TrimmedText("Make", 50, required: true);
        RuleFor(request => request.Model).TrimmedText("Model", 50, required: true);
        RuleFor(request => request.Trim).TrimmedText("Trim", 50, required: false);
        RuleFor(request => request.Year)
            .NotNull().WithMessage("Year is required.")
            .Must(year => year is null || (year >= 1900 && year <= Today().Year + 1))
            .WithMessage(_ => $"Year must be between 1900 and {Today().Year + 1}.");
        RuleFor(request => request.PurchasedOn)
            .Must(date => date is null || date <= Today())
            .WithMessage("Date purchased cannot be in the future.");
        RuleFor(request => request.LeaseEndsOn)
            .Must((request, date) => date is null || request.Ownership == OwnershipType.Leased)
            .WithMessage("Date lease ends applies only to leased vehicles.")
            .Must((request, date) => date is null || request.PurchasedOn is null || date >= request.PurchasedOn)
            .WithMessage("Date lease ends cannot be before the date purchased.");
        RuleFor(request => request.Engine).TrimmedText("Engine", 100, required: false);
        RuleFor(request => request.Horsepower).InclusiveBetween(1, 3000).WithMessage("Horsepower must be between 1 and 3,000.");
        RuleFor(request => request.Mileage).InclusiveBetween(0, 9_999_999).WithMessage("Mileage must be between 0 and 9,999,999.");
        RuleFor(request => request.Color).TrimmedText("Color", 30, required: false);
        RuleFor(request => request.Seats).InclusiveBetween(1, 20).WithMessage("Seats must be between 1 and 20.");
        RuleFor(request => request.Doors).InclusiveBetween(1, 6).WithMessage("Doors must be between 1 and 6.");
        RuleFor(request => request.WheelSize)
            .InclusiveBetween(10m, 30m).WithMessage("Wheel size must be between 10 and 30 inches.")
            .Must(size => size is null || decimal.Round(size.Value, 1) == size.Value).WithMessage("Wheel size can have at most one decimal place.");
        RuleFor(request => request.TireSize).TrimmedText("Tire size", 20, required: false);
    }

    [GeneratedRegex("^[A-HJ-NPR-Z0-9]{17}$")]
    private static partial Regex VinPattern();
}
