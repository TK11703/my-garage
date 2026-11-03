namespace MyGarage.Database.Inspections;

public sealed record InspectionRecord
{
    public Guid Id { get; init; }

    public Guid VehicleId { get; init; }

    // Stored text: "safety" or "emissions".
    public string InspectionType { get; init; } = string.Empty;

    public DateOnly InspectionDate { get; init; }

    public decimal Cost { get; init; }

    public DateOnly? NextDueDate { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public string UpdatedBy { get; init; } = string.Empty;

    public bool FromEmail { get; init; }
}
