namespace MyGarage.Shared.Inspections;

public sealed class InspectionDto
{
    public required Guid Id { get; init; }

    public required Guid VehicleId { get; init; }

    public required InspectionType Type { get; init; }

    public required DateOnly InspectionDate { get; init; }

    public required decimal Cost { get; init; }

    public DateOnly? NextDueDate { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public bool FromEmail { get; init; }
}
