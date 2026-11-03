namespace MyGarage.Database.Taxes;

public sealed record TaxRecord
{
    public Guid Id { get; init; }

    public Guid VehicleId { get; init; }

    // Stored text: personal_property, lease or purchase.
    public string TaxType { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    // Stored text: once, monthly, bi_annual or annual.
    public string Recurrence { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public string UpdatedBy { get; init; } = string.Empty;

    public bool FromEmail { get; init; }
}
