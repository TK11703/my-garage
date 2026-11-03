using MyGarage.Shared.Taxes;

namespace MyGarage.Web.Taxes;

public static class TaxFormat
{
    public static string TypeLabel(TaxType type) =>
        type switch
        {
            TaxType.PersonalProperty => "Personal Property",
            TaxType.Lease => "Lease",
            TaxType.Purchase => "Purchase",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown tax type."),
        };

    public static string RecurrenceLabel(TaxRecurrence recurrence) =>
        recurrence switch
        {
            TaxRecurrence.Once => "Once",
            TaxRecurrence.Monthly => "Monthly",
            TaxRecurrence.BiAnnual => "Bi-Annual",
            TaxRecurrence.Annual => "Annual",
            _ => throw new ArgumentOutOfRangeException(nameof(recurrence), recurrence, "Unknown tax recurrence."),
        };

    public static string EditName(TaxDto tax) => $"Edit {TypeLabel(tax.Type)} tax";
}
