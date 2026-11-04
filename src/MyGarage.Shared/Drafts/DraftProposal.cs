using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Insurance;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Taxes;

namespace MyGarage.Shared.Drafts;

// The proposed record: exactly the section for Kind is populated. Sections reuse the shapes of the existing request DTOs.
public sealed class DraftProposal
{
    public DraftKind? Kind { get; init; }

    public ServiceRecordProposal? ServiceRecord { get; init; }

    public FuelEnergyProposal? FuelEnergy { get; init; }

    public InspectionProposal? Inspection { get; init; }

    public RegistrationProposal? Registration { get; init; }

    public InsuranceProposal? Insurance { get; init; }

    public TaxProposal? Tax { get; init; }
}

public sealed class ServiceRecordProposal
{
    public DateOnly? DateServiced { get; init; }

    public int? OdometerReading { get; init; }

    public string? ShopOrPerson { get; init; }

    public IReadOnlyList<ServiceLineItemRequest> LineItems { get; init; } = [];

    // The tax for the whole record; never a line item.
    public decimal? Tax { get; init; }

    // The total printed on the document; only used to warn when the line items add up differently. Never stored on the record.
    public decimal? DocumentTotal { get; init; }
}

public sealed class FuelEnergyProposal
{
    public FuelEnergyKind? Kind { get; init; }

    public DateOnly? Date { get; init; }

    public decimal? UnitPrice { get; init; }

    public decimal? Quantity { get; init; }

    public decimal? TotalCost { get; init; }
}

public sealed class InspectionProposal
{
    public InspectionType? Type { get; init; }

    public DateOnly? Date { get; init; }

    public decimal? Cost { get; init; }

    public DateOnly? NextDueDate { get; init; }
}

public sealed class RegistrationProposal
{
    // Two-letter USPS code.
    public string? State { get; init; }

    public decimal? Cost { get; init; }

    public DateOnly? ExpirationDate { get; init; }
}

public sealed class InsuranceProposal
{
    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    public InsuranceDuration? Duration { get; init; }

    // The premium paid each time.
    public decimal? Cost { get; init; }
}

public sealed class TaxProposal
{
    public TaxType? Type { get; init; }

    public decimal? Amount { get; init; }

    public TaxRecurrence? Recurrence { get; init; }
}
