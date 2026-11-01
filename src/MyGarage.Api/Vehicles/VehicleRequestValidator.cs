using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles;

public sealed class VehicleRequestValidator : AbstractValidator<VehicleRequest>
{
    public VehicleRequestValidator()
    {
        RuleFor(request => request.Nickname).TrimmedText("Nickname", 100, required: true);
        RuleFor(request => request.Make).TrimmedText("Make", 50, required: true);
        RuleFor(request => request.Model).TrimmedText("Model", 50, required: true);
        RuleFor(request => request.Trim).TrimmedText("Trim", 50, required: false);
        RuleFor(request => request.Color).TrimmedText("Color", 30, required: false);
        RuleFor(request => request.Mileage).InclusiveBetween(0, 9_999_999).WithMessage("Mileage must be between 0 and 9,999,999.");
        RuleFor(request => request.Doors).InclusiveBetween(1, 6).WithMessage("Doors must be between 1 and 6.");
    }
}
