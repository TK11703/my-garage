namespace MyGarage.Api.EmailIntake.Extraction;

// Reads one email text or one attached document and describes the maintenance item in it; never decides anything.
public interface IDraftExtractor
{
    // False when no endpoint or deployment is configured; ExtractAsync is then never called.
    bool IsEnabled { get; }

    // Returns a failed result (FailureReason set) for service errors instead of throwing.
    Task<ExtractionResult> ExtractAsync(ExtractionInput input, CancellationToken cancellationToken);
}

public enum ExtractionKind
{
    None,
    ServiceRecord,
    Fuel,
    Energy,
    Inspection,
    Registration,
    Insurance,
    Tax,
}

public sealed record VehicleHint(string? Vin, int? Year, string? Make, string? Model, int? Odometer);

// The attached file, read once from Content (the caller owns and disposes the stream). ContentType is the verified type: a PDF or an image.
public sealed record ExtractionDocument(Stream Content, string ContentType, string FileName);

// The email text is context only when a document is present.
public sealed record ExtractionInput(string? Subject, string? Text, DateTimeOffset? ReceivedAt, ExtractionDocument? Document = null);

public sealed record ExtractedPart(string? Name, decimal? Quantity, decimal? UnitPrice);

public sealed record ExtractedLineItem(
    string? Description,
    decimal? FlatCost,
    decimal? LaborHours,
    decimal? LaborRate,
    IReadOnlyList<ExtractedPart>? Parts,
    decimal? LaborCost = null);

// Flat, nullable fields for every kind: dates are yyyy-MM-dd text and money has no currency symbol, as the model returns them.
public sealed record ExtractionResult
{
    public ExtractionKind Kind { get; init; } = ExtractionKind.None;

    public decimal? Confidence { get; init; }

    public string? Notes { get; init; }

    public VehicleHint? VehicleHint { get; init; }

    // A short reason code (never email content) when the extraction did not produce an answer.
    public string? FailureReason { get; init; }

    public bool IsFailure => FailureReason is not null;

    public string? Date { get; init; }

    public int? OdometerReading { get; init; }

    public string? ShopOrPerson { get; init; }

    public decimal? TotalCost { get; init; }

    // Service records: the tax for the whole document, never a line item.
    public decimal? Tax { get; init; }

    public decimal? UnitPrice { get; init; }

    public decimal? Quantity { get; init; }

    public string? Type { get; init; }

    public string? NextDueDate { get; init; }

    public string? State { get; init; }

    public string? ExpirationDate { get; init; }

    public string? StartDate { get; init; }

    public string? EndDate { get; init; }

    public string? Duration { get; init; }

    public string? Recurrence { get; init; }

    public IReadOnlyList<ExtractedLineItem>? LineItems { get; init; }

    public static ExtractionResult Failed(string reason) => new() { FailureReason = reason };
}
