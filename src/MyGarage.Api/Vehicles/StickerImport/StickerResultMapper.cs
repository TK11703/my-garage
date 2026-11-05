using FluentValidation;
using MyGarage.Api.Vehicles.Tags;
using MyGarage.Database.Lookups;
using MyGarage.Database.Tags;
using MyGarage.Shared.Vehicles;
using MyGarage.Shared.Vehicles.StickerImport;

namespace MyGarage.Api.Vehicles.StickerImport;

// Turns what the model read into values safe to put in the form (research R6, R7): every value must pass the same rules as a typed one,
// lookup texts become the user's own items, and proposed features are capped and matched to the vehicle's existing ones.
public sealed class StickerResultMapper(IValidator<VehicleRequest> vehicleValidator)
{
    // Fewer filled fields than this earns the "only some details could be read" notice.
    private const int FewFieldsThreshold = 5;

    private static readonly (string Property, string Label)[] ValidatedFields =
    [
        (nameof(VehicleRequest.Year), "Year"),
        (nameof(VehicleRequest.Make), "Make"),
        (nameof(VehicleRequest.Model), "Model"),
        (nameof(VehicleRequest.Trim), "Trim"),
        (nameof(VehicleRequest.Vin), "VIN"),
        (nameof(VehicleRequest.Engine), "Engine"),
        (nameof(VehicleRequest.Horsepower), "Horsepower"),
        (nameof(VehicleRequest.Color), "Color"),
        (nameof(VehicleRequest.Seats), "Seats"),
        (nameof(VehicleRequest.Doors), "Doors"),
        (nameof(VehicleRequest.WheelSize), "Wheel size"),
        (nameof(VehicleRequest.TireSize), "Tire size"),
    ];

    // shownLookups: the caller's shown items only. existingTags: the edited vehicle's features, in the order whose numbers the model was given.
    public StickerImportResultDto Map(
        StickerExtraction extraction,
        IReadOnlyList<LookupItemRecord> shownLookups,
        IReadOnlyList<VehicleTagRecord> existingTags,
        bool duplicateVin)
    {
        if (!extraction.IsWindowSticker)
        {
            return new StickerImportResultDto
            {
                Outcome = StickerImportOutcome.NoDetailsFound,
                Notices =
                [
                    new StickerNoticeDto { Code = StickerNoticeCode.NotAStickerLikely, Message = "That file does not look like a window sticker." },
                    new StickerNoticeDto { Code = StickerNoticeCode.NoDetails, Message = "No vehicle details were found in that file." },
                ],
            };
        }

        var request = new VehicleRequest
        {
            Year = extraction.Year,
            Make = Clean(extraction.Make),
            Model = Clean(extraction.Model),
            Trim = Clean(extraction.Trim),
            Vin = CleanVin(extraction.Vin),
            Engine = Clean(extraction.Engine),
            Horsepower = extraction.Horsepower,
            Color = Clean(extraction.ExteriorColor),
            Seats = extraction.Seats,
            Doors = extraction.Doors,
            WheelSize = extraction.WheelSizeInches,
            TireSize = Clean(extraction.TireSize),
        };

        // A value that fails a rule is dropped; a missing required value is not an error here (it applies when the user saves).
        var failing = vehicleValidator.Validate(request).Errors.Select(error => error.PropertyName).ToHashSet();
        var dropped = new List<string>();
        foreach (var (property, label) in ValidatedFields)
        {
            if (failing.Contains(property) && Value(request, property) is not null)
            {
                request = Without(request, property);
                dropped.Add(label);
            }
        }

        var (vehicleTypeId, vehicleTypeText) = MatchLookup(shownLookups, LookupKind.VehicleType, extraction.BodyStyle);
        var (fuelTypeId, fuelTypeText) = MatchLookup(shownLookups, LookupKind.FuelType, extraction.FuelType);
        var (transmissionId, transmissionText) = MatchLookup(shownLookups, LookupKind.Transmission, extraction.Transmission);
        var (drivetrainId, drivetrainText) = MatchLookup(shownLookups, LookupKind.Drivetrain, extraction.Drivetrain);

        var (features, leftOut) = MapFeatures(extraction.Features, existingTags);

        var filled = ValidatedFields.Count(field => Value(request, field.Property) is not null)
                     + new[] { vehicleTypeId, fuelTypeId, transmissionId, drivetrainId }.Count(id => id is not null);

        var notices = new List<StickerNoticeDto>();
        if (filled == 0 && features.Count == 0)
        {
            notices.Add(new StickerNoticeDto { Code = StickerNoticeCode.NoDetails, Message = "No vehicle details were found in that file." });
        }
        else
        {
            if (dropped.Count > 0)
            {
                notices.Add(new StickerNoticeDto
                {
                    Code = StickerNoticeCode.UnreadableValues,
                    Message = $"These values could not be used and were left empty: {string.Join(", ", dropped)}.",
                });
            }

            if (leftOut > 0)
            {
                notices.Add(new StickerNoticeDto
                {
                    Code = StickerNoticeCode.FeaturesLeftOut,
                    Message = $"{leftOut} {(leftOut == 1 ? "feature was" : "features were")} left out to stay within {VehicleTagRepository.MaxTagsPerVehicle} per vehicle.",
                });
            }

            if (filled < FewFieldsThreshold)
            {
                notices.Add(new StickerNoticeDto { Code = StickerNoticeCode.PartiallyRead, Message = "Only some details could be read from the sticker." });
            }
        }

        if (extraction.VehiclesDetected > 1)
        {
            notices.Add(new StickerNoticeDto { Code = StickerNoticeCode.MultipleVehicles, Message = "The file seems to show more than one vehicle; only one was read." });
        }

        if (duplicateVin && request.Vin is not null)
        {
            notices.Add(new StickerNoticeDto { Code = StickerNoticeCode.DuplicateVin, Message = "A vehicle with this VIN is already in your garage." });
        }

        var outcome = filled == 0 && features.Count == 0
            ? StickerImportOutcome.NoDetailsFound
            : dropped.Count > 0 || leftOut > 0 || extraction.VehiclesDetected > 1 ? StickerImportOutcome.Partial : StickerImportOutcome.Filled;

        return new StickerImportResultDto
        {
            Outcome = outcome,
            Year = request.Year,
            Make = request.Make,
            Model = request.Model,
            Trim = request.Trim,
            Vin = request.Vin,
            VehicleTypeId = vehicleTypeId,
            FuelTypeId = fuelTypeId,
            TransmissionId = transmissionId,
            DrivetrainId = drivetrainId,
            VehicleTypeText = vehicleTypeText,
            FuelTypeText = fuelTypeText,
            TransmissionText = transmissionText,
            DrivetrainText = drivetrainText,
            Engine = request.Engine,
            Horsepower = request.Horsepower,
            Color = request.Color,
            Seats = request.Seats,
            Doors = request.Doors,
            WheelSize = request.WheelSize,
            TireSize = request.TireSize,
            Features = features,
            Notices = notices,
            FieldsFilled = filled,
        };
    }

    private static string? Clean(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string? CleanVin(string? value)
    {
        var text = value is null ? null : string.Concat(value.Where(character => !char.IsWhiteSpace(character))).ToUpperInvariant();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private static (Guid? Id, string? Text) MatchLookup(IReadOnlyList<LookupItemRecord> items, string kind, string? text)
    {
        if (Clean(text) is not { } cleaned)
        {
            return (null, null);
        }

        var normalized = Normalize(cleaned);
        var match = items.FirstOrDefault(item => item.Kind == kind && Normalize(item.Name) == normalized);
        return match is null ? (null, cleaned.Length > 100 ? cleaned[..100] : cleaned) : (match.Id, null);
    }

    private static (List<StickerFeatureDto> Features, int LeftOut) MapFeatures(IReadOnlyList<ExtractedFeature> extracted, IReadOnlyList<VehicleTagRecord> existingTags)
    {
        var features = new List<StickerFeatureDto>();
        var seenKeys = new HashSet<string>();
        var replaced = new HashSet<Guid>();
        var remaining = VehicleTagRepository.MaxTagsPerVehicle - existingTags.Count;
        var leftOut = 0;

        foreach (var feature in extracted)
        {
            var name = Shorten(Clean(feature.Name));
            var key = FeatureKey.Of(name);
            if (name is null || key.Length == 0 || !seenKeys.Add(key))
            {
                continue;
            }

            var replaces = new List<VehicleTagRecord>();
            for (var i = 0; i < existingTags.Count; i++)
            {
                var tag = existingTags[i];
                var named = feature.SameAs.Contains(i + 1);
                if ((named || FeatureKey.Of(tag.Name) == key) && !replaces.Contains(tag))
                {
                    replaces.Add(tag);
                }
            }

            var freed = replaces.Count(tag => !replaced.Contains(tag.Id));
            if (remaining - 1 + freed < 0)
            {
                leftOut++;
                continue;
            }

            remaining += freed - 1;
            replaces.ForEach(tag => replaced.Add(tag.Id));
            features.Add(new StickerFeatureDto
            {
                Name = name,
                ReplacesTagIds = [.. replaces.Select(tag => tag.Id)],
                ReplacesNames = [.. replaces.Select(tag => tag.Name)],
            });
        }

        return (features, leftOut);
    }

    // Longer names are cut at the last word boundary within the limit, or hard at the limit when there is none.
    private static string? Shorten(string? name)
    {
        if (name is null)
        {
            return null;
        }

        name = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        const int max = AddVehicleTagRequestValidator.MaxNameLength;
        if (name.Length <= max)
        {
            return name;
        }

        var cut = name[..max];
        if (name[max] == ' ')
        {
            return cut;
        }

        var space = cut.LastIndexOf(' ');
        return space > 0 ? cut[..space] : cut;
    }

    private static object? Value(VehicleRequest request, string property) => property switch
    {
        nameof(VehicleRequest.Year) => request.Year,
        nameof(VehicleRequest.Make) => request.Make,
        nameof(VehicleRequest.Model) => request.Model,
        nameof(VehicleRequest.Trim) => request.Trim,
        nameof(VehicleRequest.Vin) => request.Vin,
        nameof(VehicleRequest.Engine) => request.Engine,
        nameof(VehicleRequest.Horsepower) => request.Horsepower,
        nameof(VehicleRequest.Color) => request.Color,
        nameof(VehicleRequest.Seats) => request.Seats,
        nameof(VehicleRequest.Doors) => request.Doors,
        nameof(VehicleRequest.WheelSize) => request.WheelSize,
        nameof(VehicleRequest.TireSize) => request.TireSize,
        _ => null,
    };

    private static VehicleRequest Without(VehicleRequest request, string property) => property switch
    {
        nameof(VehicleRequest.Year) => request with { Year = null },
        nameof(VehicleRequest.Make) => request with { Make = null },
        nameof(VehicleRequest.Model) => request with { Model = null },
        nameof(VehicleRequest.Trim) => request with { Trim = null },
        nameof(VehicleRequest.Vin) => request with { Vin = null },
        nameof(VehicleRequest.Engine) => request with { Engine = null },
        nameof(VehicleRequest.Horsepower) => request with { Horsepower = null },
        nameof(VehicleRequest.Color) => request with { Color = null },
        nameof(VehicleRequest.Seats) => request with { Seats = null },
        nameof(VehicleRequest.Doors) => request with { Doors = null },
        nameof(VehicleRequest.WheelSize) => request with { WheelSize = null },
        nameof(VehicleRequest.TireSize) => request with { TireSize = null },
        _ => request,
    };
}
