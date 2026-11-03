using MyGarage.Shared.Taxes;

namespace MyGarage.Web.Taxes;

public sealed class TaxFormModel
{
    public TaxType? Type { get; set; }

    public decimal? Amount { get; set; }

    public TaxRecurrence? Recurrence { get; set; }

    public TaxRequest ToRequest() => new() { Type = Type, Amount = Amount, Recurrence = Recurrence };

    public static TaxFormModel From(TaxDto tax) =>
        new()
        {
            Type = tax.Type,
            Amount = tax.Amount,
            Recurrence = tax.Recurrence,
        };
}
