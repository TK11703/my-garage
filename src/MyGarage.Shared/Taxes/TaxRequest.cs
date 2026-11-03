namespace MyGarage.Shared.Taxes;

public sealed class TaxRequest
{
    public TaxType? Type { get; init; }

    // The amount paid each time, not the yearly total.
    public decimal? Amount { get; init; }

    public TaxRecurrence? Recurrence { get; init; }
}
