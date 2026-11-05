using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Api.Vehicles.Tags;
using MyGarage.Database.Tags;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles.StickerImport;

public sealed class StickerFeatureRequestValidator : AbstractValidator<StickerFeatureRequest>
{
    // An existing vehicle has at most this many features, so it cannot have more to replace.
    public const int MaxReplaced = VehicleTagRepository.MaxTagsPerVehicle;

    public StickerFeatureRequestValidator()
    {
        RuleFor(request => request.Name).TrimmedText("Feature", AddVehicleTagRequestValidator.MaxNameLength, required: true);
        RuleFor(request => request.ReplacesTagIds)
            .Must(ids => ids is null || ids.Count <= MaxReplaced)
            .WithMessage($"A feature can replace at most {MaxReplaced} existing features.");
    }
}
