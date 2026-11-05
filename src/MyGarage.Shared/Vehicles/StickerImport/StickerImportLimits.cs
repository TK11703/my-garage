namespace MyGarage.Shared.Vehicles.StickerImport;

public static class StickerImportLimits
{
    public const long MaxFileBytes = 10_485_760;

    // Mirrors the API's per-vehicle feature cap (VehicleTagRepository.MaxTagsPerVehicle; a test keeps them equal).
    public const int MaxFeaturesPerVehicle = 100;

    public const string Pdf = "application/pdf";

    public static readonly IReadOnlyList<string> AcceptedContentTypes = [Pdf, "image/jpeg", "image/png", "image/webp"];

    // The value for an HTML file input's accept attribute.
    public const string Accept = "application/pdf,image/jpeg,image/png,image/webp";
}
