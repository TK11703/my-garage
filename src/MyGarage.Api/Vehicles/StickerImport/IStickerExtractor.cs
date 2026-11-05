namespace MyGarage.Api.Vehicles.StickerImport;

// Reads one window sticker (PDF or image) and reports what is printed on it; never decides anything.
public interface IStickerExtractor
{
    // False when no endpoint or deployment is configured; ExtractAsync is then never called.
    bool IsEnabled { get; }

    // Returns a failed result (FailureReason set) for service errors instead of throwing.
    Task<StickerExtraction> ExtractAsync(StickerInput input, CancellationToken cancellationToken);
}

// The verified file, read once from Content (the caller owns and disposes the stream), and the edited vehicle's own feature names.
public sealed record StickerInput(Stream Content, string ContentType, string FileName, IReadOnlyList<string>? ExistingFeatures);

// A feature as printed, and the 1-based numbers of the vehicle's existing features the model says are the same equipment.
public sealed record ExtractedFeature(string? Name, IReadOnlyList<int> SameAs);

// Flat, nullable fields as the model returned them: text is unvalidated and units are as asked (inches, plain numbers).
public sealed record StickerExtraction
{
    // A short reason code (never file content) when the extraction did not produce an answer.
    public string? FailureReason { get; init; }

    public bool IsFailure => FailureReason is not null;

    public static StickerExtraction Failed(string reason) => new() { FailureReason = reason };

    public bool IsWindowSticker { get; init; }

    public int VehiclesDetected { get; init; }

    public int? Year { get; init; }

    public string? Make { get; init; }

    public string? Model { get; init; }

    public string? Trim { get; init; }

    public string? Vin { get; init; }

    public string? BodyStyle { get; init; }

    public string? Engine { get; init; }

    public int? Horsepower { get; init; }

    public string? FuelType { get; init; }

    public string? Transmission { get; init; }

    public string? Drivetrain { get; init; }

    public string? ExteriorColor { get; init; }

    public int? Seats { get; init; }

    public int? Doors { get; init; }

    public decimal? WheelSizeInches { get; init; }

    public string? TireSize { get; init; }

    public IReadOnlyList<ExtractedFeature> Features { get; init; } = [];
}
