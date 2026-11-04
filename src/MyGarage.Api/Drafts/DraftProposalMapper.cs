using MyGarage.Api.FuelEnergyEntries;
using MyGarage.Api.Inspections;
using MyGarage.Api.Insurance;
using MyGarage.Api.ServiceRecords;
using MyGarage.Api.Taxes;
using MyGarage.Database.Drafts;
using MyGarage.Database.ServiceRecords;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Insurance;
using MyGarage.Shared.Registrations;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Taxes;

namespace MyGarage.Api.Drafts;

// The explicit mapping from a proposal section to the request DTO of the matching manual form, and from a validated proposal to the values the
// accept transaction stores. The existing validators judge the request DTOs, so an accepted draft is exactly as valid as a hand-entered record.
public static class DraftProposalMapper
{
    public static ServiceRecordRequest ToRequest(ServiceRecordProposal section) =>
        new() { DateServiced = section.DateServiced, OdometerReading = section.OdometerReading, ShopOrPerson = section.ShopOrPerson, Tax = section.Tax };

    // The draft kind decides whether the entry is fuel or charging, so the section's own kind cannot contradict it.
    public static FuelEnergyEntryRequest ToRequest(DraftKind kind, FuelEnergyProposal section) =>
        new()
        {
            Kind = kind == DraftKind.Energy ? FuelEnergyKind.Energy : FuelEnergyKind.Fuel,
            EntryDate = section.Date,
            UnitPrice = section.UnitPrice,
            Quantity = section.Quantity,
            TotalCost = section.TotalCost,
        };

    public static InspectionRequest ToRequest(InspectionProposal section) =>
        new() { Type = section.Type, InspectionDate = section.Date, Cost = section.Cost, NextDueDate = section.NextDueDate };

    public static RegistrationRequest ToRequest(RegistrationProposal section) =>
        new() { State = section.State, Cost = section.Cost, ExpirationDate = section.ExpirationDate };

    public static InsurancePolicyRequest ToRequest(InsuranceProposal section) =>
        new() { StartDate = section.StartDate, EndDate = section.EndDate, Duration = section.Duration, Cost = section.Cost };

    public static TaxRequest ToRequest(TaxProposal section) =>
        new() { Type = section.Type, Amount = section.Amount, Recurrence = section.Recurrence };

    // The proposal must already have passed the validators; this only converts.
    public static AcceptedRecordValues ToValues(DraftProposal proposal)
    {
        var kind = proposal.Kind ?? throw new InvalidOperationException("The proposal has no kind.");
        switch (kind)
        {
            case DraftKind.ServiceRecord:
                var service = proposal.ServiceRecord ?? new ServiceRecordProposal();
                return new ServiceRecordValues(
                    service.DateServiced!.Value,
                    service.OdometerReading!.Value,
                    ServiceRecordMapper.CleanShop(service.ShopOrPerson),
                    [.. LinesOf(service).Select(ServiceRecordMapper.ToInput)],
                    ServiceRecordMapper.CleanTax(service.Tax));

            case DraftKind.Fuel:
            case DraftKind.Energy:
                var fuel = proposal.FuelEnergy ?? new FuelEnergyProposal();
                return new FuelEnergyValues(
                    kind == DraftKind.Energy ? FuelEnergyEntryMapper.ToStoredKind(FuelEnergyKind.Energy) : FuelEnergyEntryMapper.ToStoredKind(FuelEnergyKind.Fuel),
                    fuel.Date!.Value,
                    fuel.UnitPrice!.Value,
                    fuel.Quantity!.Value,
                    FuelEnergyTotal.Resolve(fuel.UnitPrice!.Value, fuel.Quantity!.Value, fuel.TotalCost));

            case DraftKind.Inspection:
                var inspection = proposal.Inspection ?? new InspectionProposal();
                return new InspectionValues(
                    InspectionMapper.ToStoredType(inspection.Type!.Value), inspection.Date!.Value, inspection.Cost!.Value, inspection.NextDueDate);

            case DraftKind.Registration:
                var registration = proposal.Registration ?? new RegistrationProposal();
                return new RegistrationValues(registration.State!, registration.Cost!.Value, registration.ExpirationDate!.Value);

            case DraftKind.Insurance:
                var insurance = proposal.Insurance ?? new InsuranceProposal();
                return new InsuranceValues(
                    insurance.StartDate!.Value,
                    insurance.EndDate!.Value,
                    InsurancePolicyMapper.ToStoredDuration(insurance.Duration!.Value),
                    insurance.Cost!.Value);

            case DraftKind.Tax:
                var tax = proposal.Tax ?? new TaxProposal();
                return new TaxValues(
                    TaxMapper.ToStoredType(tax.Type!.Value), tax.Amount!.Value, TaxMapper.ToStoredRecurrence(tax.Recurrence!.Value));

            default:
                throw new ArgumentOutOfRangeException(nameof(proposal), kind, "Unknown draft kind.");
        }
    }

    // The proposed total or cost, when the proposal has one: the document total or the sum of the line items for a service record.
    public static decimal? HeadlineTotal(DraftProposal proposal) =>
        proposal.Kind switch
        {
            DraftKind.ServiceRecord => proposal.ServiceRecord is { } service ? service.DocumentTotal ?? RecordTotal(service) : null,
            DraftKind.Fuel or DraftKind.Energy => proposal.FuelEnergy is { } fuel
                ? fuel.TotalCost ?? (fuel.UnitPrice is { } price && fuel.Quantity is { } quantity ? FuelEnergyTotal.Resolve(price, quantity, null) : null)
                : null,
            DraftKind.Inspection => proposal.Inspection?.Cost,
            DraftKind.Registration => proposal.Registration?.Cost,
            DraftKind.Insurance => proposal.Insurance?.Cost,
            DraftKind.Tax => proposal.Tax?.Amount,
            _ => null,
        };

    // The sum of the line items that can be priced; null when there are none.
    public static decimal? LineItemsTotal(ServiceRecordProposal service)
    {
        var lines = LinesOf(service);
        if (lines.Count == 0)
        {
            return null;
        }

        return lines.Sum(line => ServiceCostCalculator.LineTotal(
            line.FlatCost,
            line.LaborHours,
            line.LaborRate,
            line.LaborCost,
            (line.Parts ?? [])
                .Where(part => part.Quantity is not null && part.UnitPrice is not null)
                .Select(part => (part.Quantity!.Value, part.UnitPrice!.Value))));
    }

    // What the accepted record's total would be: the line items plus the tax; null when there are no line items to compare.
    public static decimal? RecordTotal(ServiceRecordProposal service) =>
        LineItemsTotal(service) is { } lines ? lines + (service.Tax ?? 0m) : null;

    // A JSON null for the list reads as an empty list.
    public static IReadOnlyList<ServiceLineItemRequest> LinesOf(ServiceRecordProposal service) => service.LineItems ?? [];

    // The date and the total a probable-duplicate lookup compares; either may be null when the proposal does not have it (or the kind has none).
    public static (DateOnly? Date, decimal? Total) DateAndTotal(DraftProposal proposal) =>
        proposal.Kind switch
        {
            DraftKind.ServiceRecord => (proposal.ServiceRecord?.DateServiced, proposal.ServiceRecord is { } service ? RecordTotal(service) : null),
            DraftKind.Fuel or DraftKind.Energy => (proposal.FuelEnergy?.Date, HeadlineTotal(proposal)),
            DraftKind.Inspection => (proposal.Inspection?.Date, proposal.Inspection?.Cost),
            DraftKind.Registration => (proposal.Registration?.ExpirationDate, proposal.Registration?.Cost),
            DraftKind.Insurance => (proposal.Insurance?.StartDate, proposal.Insurance?.Cost),
            _ => (null, null),
        };
}
