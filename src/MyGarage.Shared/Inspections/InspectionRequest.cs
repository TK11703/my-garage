namespace MyGarage.Shared.Inspections;

public sealed class InspectionRequest
{
    public InspectionType? Type { get; init; }

    public DateOnly? InspectionDate { get; init; }

    public decimal? Cost { get; init; }

    // When the next inspection is due; optional.
    public DateOnly? NextDueDate { get; init; }
}
