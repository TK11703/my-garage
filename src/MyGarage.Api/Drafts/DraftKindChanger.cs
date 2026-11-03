using MyGarage.Shared.Drafts;
using MyGarage.Shared.FuelEnergyEntries;

namespace MyGarage.Api.Drafts;

// Re-presents a proposal as another record kind: date, odometer, shop, and the document total or cost move to the new section where the field
// exists, and everything else is cleared.
public static class DraftKindChanger
{
    public static DraftProposal Change(DraftProposal proposal, DraftKind target)
    {
        if (proposal.Kind == target)
        {
            return proposal;
        }

        // Fuel and charging share one shape, so only the entry kind changes.
        if (proposal.Kind is DraftKind.Fuel or DraftKind.Energy && target is DraftKind.Fuel or DraftKind.Energy)
        {
            var source = proposal.FuelEnergy ?? new FuelEnergyProposal();
            return new DraftProposal
            {
                Kind = target,
                FuelEnergy = new FuelEnergyProposal
                {
                    Kind = EntryKind(target),
                    Date = source.Date,
                    UnitPrice = source.UnitPrice,
                    Quantity = source.Quantity,
                    TotalCost = source.TotalCost,
                },
            };
        }

        var date = EventDate(proposal);
        var total = DraftProposalMapper.HeadlineTotal(proposal);
        var service = proposal.ServiceRecord;

        return target switch
        {
            DraftKind.ServiceRecord => new DraftProposal
            {
                Kind = target,
                ServiceRecord = new ServiceRecordProposal
                {
                    DateServiced = date,
                    OdometerReading = service?.OdometerReading,
                    ShopOrPerson = service?.ShopOrPerson,
                    DocumentTotal = total,
                },
            },
            DraftKind.Fuel or DraftKind.Energy => new DraftProposal
            {
                Kind = target,
                FuelEnergy = new FuelEnergyProposal { Kind = EntryKind(target), Date = date, TotalCost = total },
            },
            DraftKind.Inspection => new DraftProposal { Kind = target, Inspection = new InspectionProposal { Date = date, Cost = total } },
            DraftKind.Registration => new DraftProposal { Kind = target, Registration = new RegistrationProposal { Cost = total } },
            DraftKind.Insurance => new DraftProposal { Kind = target, Insurance = new InsuranceProposal { StartDate = date, Cost = total } },
            DraftKind.Tax => new DraftProposal { Kind = target, Tax = new TaxProposal { Amount = total } },
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown draft kind."),
        };
    }

    private static FuelEnergyKind EntryKind(DraftKind kind) => kind == DraftKind.Energy ? FuelEnergyKind.Energy : FuelEnergyKind.Fuel;

    // The day the thing happened; a registration's expiration date is not that, so it is not carried.
    private static DateOnly? EventDate(DraftProposal proposal) =>
        proposal.Kind switch
        {
            DraftKind.ServiceRecord => proposal.ServiceRecord?.DateServiced,
            DraftKind.Fuel or DraftKind.Energy => proposal.FuelEnergy?.Date,
            DraftKind.Inspection => proposal.Inspection?.Date,
            DraftKind.Insurance => proposal.Insurance?.StartDate,
            _ => null,
        };
}
