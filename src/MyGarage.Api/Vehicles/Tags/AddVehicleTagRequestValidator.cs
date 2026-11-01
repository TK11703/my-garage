using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles.Tags;

public sealed class AddVehicleTagRequestValidator : AbstractValidator<AddVehicleTagRequest>
{
    public AddVehicleTagRequestValidator() =>
        RuleFor(request => request.Name).TrimmedText("Tag", 30, required: true);
}
