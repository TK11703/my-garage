namespace MyGarage.Shared.Insurance;

public sealed class InsurancePolicyDto
{
    public required Guid Id { get; init; }

    public required Guid VehicleId { get; init; }

    public required DateOnly StartDate { get; init; }

    public required DateOnly EndDate { get; init; }

    public required InsuranceDuration Duration { get; init; }

    public required decimal Cost { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
