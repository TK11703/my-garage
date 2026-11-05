using MyGarage.Database.Tags;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles.StickerImport;

public static class StickerFeatureMapper
{
    public static IReadOnlyList<StickerFeatureWrite> ToWrites(VehicleRequest request) =>
        request.StickerFeatures is { Count: > 0 } features
            ? [.. features.Select(feature => new StickerFeatureWrite(feature.Name!.Trim(), [.. feature.ReplacesTagIds ?? []]))]
            : [];
}
