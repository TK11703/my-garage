using System.Text.Json.Serialization;

namespace MyGarage.Shared.Vehicles.StickerImport;

[JsonConverter(typeof(JsonStringEnumConverter<StickerNoticeCode>))]
public enum StickerNoticeCode
{
    NoDetails,
    PartiallyRead,
    UnreadableValues,
    FeaturesLeftOut,
    MultipleVehicles,
    DuplicateVin,
    NotAStickerLikely,
}

public sealed class StickerNoticeDto
{
    public required StickerNoticeCode Code { get; init; }

    // Short text that never contains the VIN or other extracted values.
    public required string Message { get; init; }
}
