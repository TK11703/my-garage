namespace MyGarage.Shared.Taxes;

public sealed class TaxDto
{
    public required Guid Id { get; init; }

    public required Guid VehicleId { get; init; }

    public required TaxType Type { get; init; }

    public required decimal Amount { get; init; }

    public required TaxRecurrence Recurrence { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public bool FromEmail { get; init; }
}
