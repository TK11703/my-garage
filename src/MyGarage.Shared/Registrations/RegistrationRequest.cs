namespace MyGarage.Shared.Registrations;

public sealed class RegistrationRequest
{
    // Two-letter USPS code of the registering state or DC.
    public string? State { get; init; }

    public decimal? Cost { get; init; }

    public DateOnly? ExpirationDate { get; init; }
}
