using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles.Tags;

public sealed class AddVehicleTagRequestValidator : AbstractValidator<AddVehicleTagRequest>
{
    public const int MaxNameLength = 30;

    public AddVehicleTagRequestValidator() =>
        RuleFor(request => request.Name).TrimmedText("Feature", MaxNameLength, required: true);
}
