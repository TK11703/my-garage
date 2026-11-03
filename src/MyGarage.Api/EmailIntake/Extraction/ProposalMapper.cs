using System.Globalization;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Insurance;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Taxes;

namespace MyGarage.Api.EmailIntake.Extraction;

// Turns the untrusted flat extraction into a DraftProposal: text is trimmed and cut to the column lengths, money is rounded,
// unparseable dates and unknown choices become null. The existing validators judge the proposal later, so nothing is trusted here.
public static class ProposalMapper
{
    public const int MaxNotesLength = 500;

    private const int MaxDescription = 200;
    private const int MaxName = 100;
    private const int MaxShop = 100;

    // Null when the extraction is not a maintenance item (kind none or unknown).
    public static DraftProposal? Map(ExtractionResult result) => result.Kind switch
    {
        ExtractionKind.ServiceRecord => new DraftProposal { Kind = DraftKind.ServiceRecord, ServiceRecord = ServiceRecord(result) },
        ExtractionKind.Fuel => new DraftProposal { Kind = DraftKind.Fuel, FuelEnergy = FuelEnergy(result, FuelEnergyKind.Fuel) },
        ExtractionKind.Energy => new DraftProposal { Kind = DraftKind.Energy, FuelEnergy = FuelEnergy(result, FuelEnergyKind.Energy) },
        ExtractionKind.Inspection => new DraftProposal { Kind = DraftKind.Inspection, Inspection = Inspection(result) },
        ExtractionKind.Registration => new DraftProposal { Kind = DraftKind.Registration, Registration = Registration(result) },
        ExtractionKind.Insurance => new DraftProposal { Kind = DraftKind.Insurance, Insurance = Insurance(result) },
        ExtractionKind.Tax => new DraftProposal { Kind = DraftKind.Tax, Tax = Tax(result) },
        _ => null,
    };

    // The model's short remark, as untrusted text.
    public static string? NotesFor(ExtractionResult result) => Text(result.Notes, MaxNotesLength);

    private static ServiceRecordProposal ServiceRecord(ExtractionResult result) =>
        new()
        {
            DateServiced = Date(result.Date),
            OdometerReading = result.OdometerReading ?? result.VehicleHint?.Odometer,
            ShopOrPerson = Text(result.ShopOrPerson, MaxShop),
            LineItems = [.. (result.LineItems ?? []).Select(LineItem)],
            DocumentTotal = Round(result.TotalCost, 2),
        };

    // Labor or parts win over a flat cost, because a line is either a flat cost or labor and parts, never both (research R12).
    private static ServiceLineItemRequest LineItem(ExtractedLineItem line)
    {
        var parts = (line.Parts ?? [])
            .Where(part => !string.IsNullOrWhiteSpace(part.Name) || part.Quantity is not null || part.UnitPrice is not null)
            .Select(part => new ServicePartRequest { Name = Text(part.Name, MaxName), Quantity = Round(part.Quantity, 2), UnitPrice = Round(part.UnitPrice, 2) })
            .ToList();
        var hasBreakdown = line.LaborHours is not null || line.LaborRate is not null || parts.Count > 0;

        return new ServiceLineItemRequest
        {
            Description = Text(line.Description, MaxDescription),
            FlatCost = hasBreakdown ? null : Round(line.FlatCost, 2),
            LaborHours = Round(line.LaborHours, 2),
            LaborRate = Round(line.LaborRate, 2),
            Parts = parts,
        };
    }

    private static FuelEnergyProposal FuelEnergy(ExtractionResult result, FuelEnergyKind kind) =>
        new()
        {
            Kind = kind,
            Date = Date(result.Date),
            UnitPrice = Round(result.UnitPrice, 4),
            Quantity = Round(result.Quantity, 3),
            TotalCost = Round(result.TotalCost, 2),
        };

    private static InspectionProposal Inspection(ExtractionResult result) =>
        new()
        {
            Type = Key(result.Type) switch
            {
                "safety" => InspectionType.Safety,
                "emissions" => InspectionType.Emissions,
                _ => null,
            },
            Date = Date(result.Date),
            Cost = Round(result.TotalCost, 2),
            NextDueDate = Date(result.NextDueDate),
        };

    private static RegistrationProposal Registration(ExtractionResult result)
    {
        var state = (result.State ?? string.Empty).Trim().ToUpperInvariant();
        return new RegistrationProposal
        {
            State = state.Length == 2 && state.All(char.IsAsciiLetterUpper) ? state : null,
            Cost = Round(result.TotalCost, 2),
            ExpirationDate = Date(result.ExpirationDate),
        };
    }

    private static InsuranceProposal Insurance(ExtractionResult result) =>
        new()
        {
            StartDate = Date(result.StartDate),
            EndDate = Date(result.EndDate),
            Duration = Key(result.Duration) switch
            {
                "monthly" => InsuranceDuration.Monthly,
                "biannual" or "semiannual" => InsuranceDuration.BiAnnual,
                "annual" or "yearly" => InsuranceDuration.Annual,
                _ => null,
            },
            Cost = Round(result.TotalCost, 2),
        };

    private static TaxProposal Tax(ExtractionResult result) =>
        new()
        {
            Type = Key(result.Type) switch
            {
                "personalproperty" => TaxType.PersonalProperty,
                "lease" => TaxType.Lease,
                "purchase" => TaxType.Purchase,
                _ => null,
            },
            Amount = Round(result.TotalCost, 2),
            Recurrence = Key(result.Recurrence) switch
            {
                "once" => TaxRecurrence.Once,
                "monthly" => TaxRecurrence.Monthly,
                "biannual" or "semiannual" => TaxRecurrence.BiAnnual,
                "annual" or "yearly" => TaxRecurrence.Annual,
                _ => null,
            },
        };

    private static DateOnly? Date(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static decimal? Round(decimal? value, int places) =>
        value is null ? null : decimal.Round(value.Value, places, MidpointRounding.AwayFromZero);

    private static string? Text(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    // Letters only, lower case: bi_annual, Bi-Annual, and BiAnnual all read the same.
    private static string Key(string? text) => new([.. (text ?? string.Empty).Where(char.IsLetter).Select(char.ToLowerInvariant)]);
}
