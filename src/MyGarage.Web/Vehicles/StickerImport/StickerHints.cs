using MyGarage.Shared.Vehicles.StickerImport;

namespace MyGarage.Web.Vehicles.StickerImport;

// The sticker's own text for a lookup it could not match to one of the user's items, shown under the empty dropdown.
public sealed record StickerHints(string? VehicleType, string? FuelType, string? Transmission, string? Drivetrain)
{
    public static readonly StickerHints None = new(null, null, null, null);

    public static StickerHints From(StickerImportResultDto? result) =>
        result is null ? None : new StickerHints(result.VehicleTypeText, result.FuelTypeText, result.TransmissionText, result.DrivetrainText);
}
