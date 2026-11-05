namespace MyGarage.Web.Vehicles.StickerImport;

// The messages shown in the Import from sticker tab (contracts/web-routes.md, Failures).
public static class StickerImportMessages
{
    public const string ChooseFile = "Choose a file first.";

    public const string Invalid = "That file is not a valid PDF, JPG, PNG, or WebP.";

    public const string TooLarge = "That file is larger than 10 MB.";

    public const string RateLimited = "You have started several imports in a row. Wait a minute and try again.";

    public const string NotConfigured = "Reading stickers is not set up here. Enter the details by hand.";

    public const string ReadFailed = "The sticker could not be read right now. Nothing was changed; try again or enter the details by hand.";

    public const string NoDetails = "No vehicle details were found in that file. Try a clearer, upright photo or a PDF, or enter the details by hand.";

    public static string? For(StickerImportFailure failure) => failure switch
    {
        StickerImportFailure.Invalid => Invalid,
        StickerImportFailure.TooLarge => TooLarge,
        StickerImportFailure.RateLimited => RateLimited,
        StickerImportFailure.NotConfigured => NotConfigured,
        StickerImportFailure.ReadFailed => ReadFailed,
        _ => null,
    };
}
