namespace MyGarage.Shared.Registrations;

public sealed class RegistrationDto
{
    public required Guid Id { get; init; }

    public required Guid VehicleId { get; init; }

    public required string State { get; init; }

    public required decimal Cost { get; init; }

    public required DateOnly ExpirationDate { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
