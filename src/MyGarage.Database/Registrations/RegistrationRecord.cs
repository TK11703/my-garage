namespace MyGarage.Database.Registrations;

public sealed record RegistrationRecord
{
    public Guid Id { get; init; }

    public Guid VehicleId { get; init; }

    // Two-letter USPS code.
    public string State { get; init; } = string.Empty;

    public decimal Cost { get; init; }

    public DateOnly ExpirationDate { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public string UpdatedBy { get; init; } = string.Empty;
}
