namespace MyGarage.Api.Vehicles.StickerImport;

// Bound from StickerImport. The endpoint and deployment are the ones email intake already uses (EmailIntake:Extraction).
public sealed class StickerImportOptions
{
    public const string SectionName = "StickerImport";

    // The user is waiting in the form, so the model call is bounded tightly.
    public int TimeoutSeconds { get; set; } = 90;

    // Reading is paid for per call, so each user may start only this many in one minute.
    public int RequestsPerMinute { get; set; } = 6;
}
