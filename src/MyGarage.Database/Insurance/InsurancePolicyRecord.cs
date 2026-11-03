namespace MyGarage.Database.Insurance;

public sealed record InsurancePolicyRecord
{
    public Guid Id { get; init; }

    public Guid VehicleId { get; init; }

    public DateOnly StartDate { get; init; }

    public DateOnly EndDate { get; init; }

    // Stored text: monthly, bi_annual or annual.
    public string Duration { get; init; } = string.Empty;

    public decimal Cost { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public string UpdatedBy { get; init; } = string.Empty;
}
