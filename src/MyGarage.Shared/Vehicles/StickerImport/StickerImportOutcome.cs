using System.Text.Json.Serialization;

namespace MyGarage.Shared.Vehicles.StickerImport;

[JsonConverter(typeof(JsonStringEnumConverter<StickerImportOutcome>))]
public enum StickerImportOutcome
{
    // Every value found on the sticker was usable.
    Filled,

    // Some values were unreadable, dropped, or left out.
    Partial,

    // A readable file that holds no vehicle details; nothing to fill in.
    NoDetailsFound,
}
