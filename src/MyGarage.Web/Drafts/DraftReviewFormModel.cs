using MyGarage.Shared.Drafts;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Insurance;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Taxes;
using MyGarage.Web.ServiceRecords;

namespace MyGarage.Web.Drafts;

// Everything the review form posts back. Only the section for the chosen record type is sent to the API.
public sealed class DraftReviewFormModel
{
    public Guid? VehicleId { get; set; }

    // service_record, fuel, energy, inspection, registration, insurance, or tax; empty while the type is still to be chosen.
    public string? KindValue { get; set; }

    public bool KeepOriginal { get; set; }

    // The form mapper leaves a collection or section null when nothing for it was posted.
    public ServiceProposalForm Service { get => field ??= new(); set; } = new();

    public FuelProposalForm Fuel { get => field ??= new(); set; } = new();

    public InspectionProposalForm Inspection { get => field ??= new(); set; } = new();

    public RegistrationProposalForm Registration { get => field ??= new(); set; } = new();

    public InsuranceProposalForm Insurance { get => field ??= new(); set; } = new();

    public TaxProposalForm Tax { get => field ??= new(); set; } = new();

    public DraftKind? Kind => DraftFormat.ParseKind(KindValue);

    public static DraftReviewFormModel From(MaintenanceDraftDto draft) =>
        From(draft.Proposal, draft.Kind, draft.Vehicle.VehicleId, keepOriginal: false);

    public static DraftReviewFormModel From(DraftProposal proposal, DraftKind? kind, Guid? vehicleId, bool keepOriginal) =>
        new()
        {
            VehicleId = vehicleId,
            KindValue = DraftFormat.KindValue(kind ?? proposal.Kind),
            KeepOriginal = keepOriginal,
            Service = ServiceProposalForm.From(proposal.ServiceRecord),
            Fuel = FuelProposalForm.From(proposal.FuelEnergy),
            Inspection = InspectionProposalForm.From(proposal.Inspection),
            Registration = RegistrationProposalForm.From(proposal.Registration),
            Insurance = InsuranceProposalForm.From(proposal.Insurance),
            Tax = TaxProposalForm.From(proposal.Tax),
        };

    // Exactly the section for the chosen kind is filled, as the API expects.
    public DraftProposal ToProposal() =>
        Kind switch
        {
            DraftKind.ServiceRecord => new DraftProposal { Kind = DraftKind.ServiceRecord, ServiceRecord = Service.ToProposal() },
            DraftKind.Fuel => new DraftProposal { Kind = DraftKind.Fuel, FuelEnergy = Fuel.ToProposal(FuelEnergyKind.Fuel) },
            DraftKind.Energy => new DraftProposal { Kind = DraftKind.Energy, FuelEnergy = Fuel.ToProposal(FuelEnergyKind.Energy) },
            DraftKind.Inspection => new DraftProposal { Kind = DraftKind.Inspection, Inspection = Inspection.ToProposal() },
            DraftKind.Registration => new DraftProposal { Kind = DraftKind.Registration, Registration = Registration.ToProposal() },
            DraftKind.Insurance => new DraftProposal { Kind = DraftKind.Insurance, Insurance = Insurance.ToProposal() },
            DraftKind.Tax => new DraftProposal { Kind = DraftKind.Tax, Tax = Tax.ToProposal() },
            _ => new DraftProposal(),
        };
}

public sealed class ServiceProposalForm
{
    public const int MaxLines = 50;
    public const int MaxParts = 25;

    public DateOnly? DateServiced { get; set; }

    public int? OdometerReading { get; set; }

    public string? ShopOrPerson { get; set; }

    public decimal? Tax { get; set; }

    // The total printed on the document; kept only so the check can warn when the lines add up differently.
    public decimal? DocumentTotal { get; set; }

    public List<ProposalLineForm> Lines { get => field ??= []; set; } = [];

    // What the lines add up to as they are entered now.
    public decimal Total => Lines.Sum(line => line.Total);

    // The lines plus the tax: what the saved record's total will be.
    public decimal RecordTotal => Total + (Tax ?? 0m);

    public static ServiceProposalForm From(ServiceRecordProposal? proposal) =>
        proposal is null
            ? new ServiceProposalForm()
            : new ServiceProposalForm
            {
                DateServiced = proposal.DateServiced,
                OdometerReading = proposal.OdometerReading,
                ShopOrPerson = proposal.ShopOrPerson,
                Tax = proposal.Tax,
                DocumentTotal = proposal.DocumentTotal,
                Lines = [.. proposal.LineItems.Select(ProposalLineForm.From)],
            };

    public ServiceRecordProposal ToProposal() =>
        new()
        {
            DateServiced = DateServiced,
            OdometerReading = OdometerReading,
            ShopOrPerson = ShopOrPerson,
            Tax = Tax,
            DocumentTotal = DocumentTotal,
            LineItems = [.. Lines.Select(line => line.ToRequest())],
        };

    public bool AddLine()
    {
        if (Lines.Count >= MaxLines)
        {
            return false;
        }

        Lines.Add(new ProposalLineForm());
        return true;
    }

    public void RemoveLine(int index)
    {
        if (index >= 0 && index < Lines.Count)
        {
            Lines.RemoveAt(index);
        }
    }

    public bool AddPart(int line)
    {
        if (line < 0 || line >= Lines.Count || Lines[line].Parts.Count >= MaxParts)
        {
            return false;
        }

        Lines[line].Parts.Add(new ServicePartRow());
        return true;
    }

    public void RemovePart(int line, int part)
    {
        if (line >= 0 && line < Lines.Count && part >= 0 && part < Lines[line].Parts.Count)
        {
            Lines[line].Parts.RemoveAt(part);
        }
    }
}

public sealed class ProposalLineForm
{
    public string? Description { get; set; }

    public PricingMode Pricing { get; set; }

    public decimal? FlatCost { get; set; }

    public decimal? LaborHours { get; set; }

    public decimal? LaborRate { get; set; }

    // Total labor, entered only when there are no hours and rate; the product of both otherwise.
    public decimal? LaborCost { get; set; }

    public bool LaborIsCalculated => LaborHours is not null && LaborRate is not null;

    public List<ServicePartRow> Parts { get => field ??= []; set; } = [];

    // The line total as the chosen pricing method gives it; missing numbers count as zero.
    public decimal Total =>
        Pricing == PricingMode.Flat
            ? FlatCost ?? 0m
            : Labor + Parts.Sum(PartTotal);

    private decimal Labor =>
        LaborIsCalculated ? Money(LaborHours, LaborRate)
        : LaborHours is null && LaborRate is null ? LaborCost ?? 0m
        : 0m;

    // What the Total labor field shows: the product when hours and rate are both given, else what was entered.
    public decimal? LaborShown => LaborIsCalculated ? Money(LaborHours, LaborRate) : LaborCost;

    public static decimal PartTotal(ServicePartRow part) => Money(part.Quantity, part.UnitPrice);

    private static decimal Money(decimal? factor, decimal? other) =>
        Math.Round((factor ?? 0m) * (other ?? 0m), 2, MidpointRounding.AwayFromZero);

    public static ProposalLineForm From(ServiceLineItemRequest line)
    {
        var breakdown = line.FlatCost is null && (line.LaborHours is not null || line.LaborRate is not null || line.LaborCost is not null || line.Parts.Count > 0);
        return new ProposalLineForm
        {
            Description = line.Description,
            Pricing = breakdown ? PricingMode.Breakdown : PricingMode.Flat,
            FlatCost = line.FlatCost,
            LaborHours = line.LaborHours,
            LaborRate = line.LaborRate,
            LaborCost = line.LaborCost,
            Parts = [.. line.Parts.Select(part => new ServicePartRow { Name = part.Name, Quantity = part.Quantity, UnitPrice = part.UnitPrice })],
        };
    }

    // Only the chosen pricing method is sent, so the other group's leftovers never reach the check.
    public ServiceLineItemRequest ToRequest() =>
        Pricing == PricingMode.Flat
            ? new ServiceLineItemRequest { Description = Description, FlatCost = FlatCost }
            : new ServiceLineItemRequest
            {
                Description = Description,
                LaborHours = LaborHours,
                LaborRate = LaborRate,
                LaborCost = LaborIsCalculated ? null : LaborCost,
                Parts = [.. Parts.Select(part => new ServicePartRequest { Name = part.Name, Quantity = part.Quantity, UnitPrice = part.UnitPrice })],
            };
}

public sealed class FuelProposalForm
{
    public DateOnly? Date { get; set; }

    public decimal? UnitPrice { get; set; }

    public decimal? Quantity { get; set; }

    public decimal? TotalCost { get; set; }

    public static FuelProposalForm From(FuelEnergyProposal? proposal) =>
        proposal is null ? new() : new() { Date = proposal.Date, UnitPrice = proposal.UnitPrice, Quantity = proposal.Quantity, TotalCost = proposal.TotalCost };

    public FuelEnergyProposal ToProposal(FuelEnergyKind kind) =>
        new() { Kind = kind, Date = Date, UnitPrice = UnitPrice, Quantity = Quantity, TotalCost = TotalCost };
}

public sealed class InspectionProposalForm
{
    public InspectionType? Type { get; set; }

    public DateOnly? Date { get; set; }

    public decimal? Cost { get; set; }

    public DateOnly? NextDueDate { get; set; }

    public static InspectionProposalForm From(InspectionProposal? proposal) =>
        proposal is null ? new() : new() { Type = proposal.Type, Date = proposal.Date, Cost = proposal.Cost, NextDueDate = proposal.NextDueDate };

    public InspectionProposal ToProposal() => new() { Type = Type, Date = Date, Cost = Cost, NextDueDate = NextDueDate };
}

public sealed class RegistrationProposalForm
{
    public string? State { get; set; }

    public decimal? Cost { get; set; }

    public DateOnly? ExpirationDate { get; set; }

    public static RegistrationProposalForm From(RegistrationProposal? proposal) =>
        proposal is null ? new() : new() { State = proposal.State, Cost = proposal.Cost, ExpirationDate = proposal.ExpirationDate };

    public RegistrationProposal ToProposal() => new() { State = State, Cost = Cost, ExpirationDate = ExpirationDate };
}

public sealed class InsuranceProposalForm
{
    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public InsuranceDuration? Duration { get; set; }

    public decimal? Cost { get; set; }

    public static InsuranceProposalForm From(InsuranceProposal? proposal) =>
        proposal is null ? new() : new() { StartDate = proposal.StartDate, EndDate = proposal.EndDate, Duration = proposal.Duration, Cost = proposal.Cost };

    public InsuranceProposal ToProposal() => new() { StartDate = StartDate, EndDate = EndDate, Duration = Duration, Cost = Cost };
}

public sealed class TaxProposalForm
{
    public TaxType? Type { get; set; }

    public decimal? Amount { get; set; }

    public TaxRecurrence? Recurrence { get; set; }

    public static TaxProposalForm From(TaxProposal? proposal) =>
        proposal is null ? new() : new() { Type = proposal.Type, Amount = proposal.Amount, Recurrence = proposal.Recurrence };

    public TaxProposal ToProposal() => new() { Type = Type, Amount = Amount, Recurrence = Recurrence };
}
