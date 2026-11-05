namespace MyGarage.Shared.Vehicles.StickerImport;

// What was read from one window sticker. Nothing here is stored; it only fills in the vehicle form.
public sealed class StickerImportResultDto
{
    public required StickerImportOutcome Outcome { get; init; }

    public int? Year { get; init; }

    public string? Make { get; init; }

    public string? Model { get; init; }

    public string? Trim { get; init; }

    // 17 characters, no I, O, or Q.
    public string? Vin { get; init; }

    // Ids of the user's shown lookup items that match the sticker; null when none matched.
    public Guid? VehicleTypeId { get; init; }

    public Guid? FuelTypeId { get; init; }

    public Guid? TransmissionId { get; init; }

    public Guid? DrivetrainId { get; init; }

    // The sticker's own text when no lookup item matched.
    public string? VehicleTypeText { get; init; }

    public string? FuelTypeText { get; init; }

    public string? TransmissionText { get; init; }

    public string? DrivetrainText { get; init; }

    public string? Engine { get; init; }

    public int? Horsepower { get; init; }

    public string? Color { get; init; }

    public int? Seats { get; init; }

    public int? Doors { get; init; }

    // Inches.
    public decimal? WheelSize { get; init; }

    public string? TireSize { get; init; }

    public IReadOnlyList<StickerFeatureDto> Features { get; init; } = [];

    public IReadOnlyList<StickerNoticeDto> Notices { get; init; } = [];

    // Vehicle fields given a value; lookup text hints do not count.
    public int FieldsFilled { get; init; }
}
