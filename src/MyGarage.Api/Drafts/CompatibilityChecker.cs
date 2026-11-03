using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using MyGarage.Database.ServiceRecords;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Insurance;
using MyGarage.Shared.Registrations;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Taxes;

namespace MyGarage.Api.Drafts;

// A field of the proposal that failed its existing validator; IsMissing when the value is simply absent.
public sealed record ProposalFailure(string Path, string Message, bool IsMissing);

// What the advisory checks need to know about the draft being checked.
public sealed record DraftCheckContext(string OwnerId, Guid? DraftId = null, Guid? VehicleId = null, string? AttachmentSha256 = null);

public interface IDraftDuplicateLookup
{
    Task<bool> HasDraftWithSameAttachmentAsync(string ownerId, Guid draftId, string sha256, CancellationToken cancellationToken);

    Task<bool> HasRecordLikeAsync(string ownerId, Guid vehicleId, string kind, DateOnly date, decimal total, CancellationToken cancellationToken);
}

// Runs the existing request validators on the current proposal of its kind and reports every field as ok, missing, or invalid, plus advisory warnings.
public sealed class CompatibilityChecker(
    IValidator<ServiceRecordRequest> serviceRecordValidator,
    IValidator<ServiceLineItemRequest> lineItemValidator,
    IValidator<FuelEnergyEntryRequest> fuelValidator,
    IValidator<InspectionRequest> inspectionValidator,
    IValidator<RegistrationRequest> registrationValidator,
    IValidator<InsurancePolicyRequest> insuranceValidator,
    IValidator<TaxRequest> taxValidator,
    IDraftDuplicateLookup duplicates)
{
    private const string ServiceRecordSection = "serviceRecord";
    private const string FuelEnergySection = "fuelEnergy";
    private const string InspectionSection = "inspection";
    private const string RegistrationSection = "registration";
    private const string InsuranceSection = "insurance";
    private const string TaxSection = "tax";

    // Every failing field of the proposal's own section, in validator order.
    public IReadOnlyList<ProposalFailure> Validate(DraftProposal proposal)
    {
        if (proposal.Kind is not { } kind)
        {
            return [new ProposalFailure("kind", "Choose a record type.", IsMissing: true)];
        }

        var failures = new List<ProposalFailure>();
        switch (kind)
        {
            case DraftKind.ServiceRecord:
                ValidateServiceRecord(proposal.ServiceRecord ?? new ServiceRecordProposal(), failures);
                break;

            case DraftKind.Fuel:
            case DraftKind.Energy:
                Add(failures, FuelEnergySection, fuelValidator.Validate(DraftProposalMapper.ToRequest(kind, proposal.FuelEnergy ?? new FuelEnergyProposal())), FuelEnergyProperty);
                break;

            case DraftKind.Inspection:
                Add(failures, InspectionSection, inspectionValidator.Validate(DraftProposalMapper.ToRequest(proposal.Inspection ?? new InspectionProposal())), InspectionProperty);
                break;

            case DraftKind.Registration:
                Add(failures, RegistrationSection, registrationValidator.Validate(DraftProposalMapper.ToRequest(proposal.Registration ?? new RegistrationProposal())), Same);
                break;

            case DraftKind.Insurance:
                Add(failures, InsuranceSection, insuranceValidator.Validate(DraftProposalMapper.ToRequest(proposal.Insurance ?? new InsuranceProposal())), Same);
                break;

            case DraftKind.Tax:
                Add(failures, TaxSection, taxValidator.Validate(DraftProposalMapper.ToRequest(proposal.Tax ?? new TaxProposal())), Same);
                break;

            default:
                failures.Add(new ProposalFailure("kind", "Choose a record type.", IsMissing: false));
                break;
        }

        return failures;
    }

    public async Task<CompatibilityDto> CheckAsync(DraftProposal proposal, DraftCheckContext context, CancellationToken cancellationToken)
    {
        var failures = Validate(proposal);
        var byPath = new Dictionary<string, (CompatibilityStatus Status, string Message)>(StringComparer.Ordinal);
        foreach (var failure in failures)
        {
            var status = failure.IsMissing ? CompatibilityStatus.Missing : CompatibilityStatus.Invalid;
            if (!byPath.TryGetValue(failure.Path, out var existing) || (existing.Status == CompatibilityStatus.Invalid && status == CompatibilityStatus.Missing))
            {
                byPath[failure.Path] = (status, failure.Message);
            }
        }

        var fields = new List<CompatibilityFieldDto>();
        foreach (var path in KnownPaths(proposal).Concat(byPath.Keys).Distinct(StringComparer.Ordinal))
        {
            fields.Add(byPath.TryGetValue(path, out var failure)
                ? new CompatibilityFieldDto { Path = path, Status = failure.Status, Message = failure.Message }
                : new CompatibilityFieldDto { Path = path, Status = CompatibilityStatus.Ok });
        }

        return new CompatibilityDto
        {
            Kind = proposal.Kind,
            IsAcceptable = proposal.Kind is not null && fields.All(field => field.Status == CompatibilityStatus.Ok),
            Fields = fields,
            Warnings = await WarningsAsync(proposal, context, cancellationToken),
        };
    }

    private void ValidateServiceRecord(ServiceRecordProposal section, List<ProposalFailure> failures)
    {
        Add(failures, ServiceRecordSection, serviceRecordValidator.Validate(DraftProposalMapper.ToRequest(section)), Same);

        var lines = DraftProposalMapper.LinesOf(section);
        if (lines.Count > ServiceRecordRepository.MaxLineItems)
        {
            failures.Add(new ProposalFailure(
                $"{ServiceRecordSection}.lineItems", $"A service record can have up to {ServiceRecordRepository.MaxLineItems} line items.", IsMissing: false));
        }

        for (var index = 0; index < lines.Count; index++)
        {
            var prefix = $"{ServiceRecordSection}.lineItems[{index}]";
            // The pricing rule belongs to the whole line, so it is reported on the line itself.
            Add(failures, prefix, lineItemValidator.Validate(lines[index]), property => property == "Pricing" ? null : property);
        }
    }

    private static string? Same(string property) => property;

    private static string? FuelEnergyProperty(string property) => property == "EntryDate" ? "Date" : property;

    private static string? InspectionProperty(string property) => property == "InspectionDate" ? "Date" : property;

    // A null mapped name means the failure is reported on the section path itself.
    private static void Add(List<ProposalFailure> failures, string section, ValidationResult result, Func<string, string?> map)
    {
        foreach (var error in result.Errors)
        {
            var property = map(error.PropertyName);
            var path = string.IsNullOrEmpty(property) ? section : $"{section}.{Camel(property)}";
            failures.Add(new ProposalFailure(path, error.ErrorMessage, IsMissingFailure(error)));
        }
    }

    private static bool IsMissingFailure(ValidationFailure error) =>
        error.ErrorCode is "NotNullValidator" or "NotEmptyValidator"
        || error.ErrorMessage.Contains(" is required", StringComparison.OrdinalIgnoreCase)
        || error.ErrorMessage.StartsWith("Enter ", StringComparison.Ordinal);

    private static string Camel(string property) =>
        string.Join('.', property.Split('.').Select(segment => segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));

    // The fields shown as ok when they pass, so the page can list everything that was checked.
    private static IEnumerable<string> KnownPaths(DraftProposal proposal)
    {
        switch (proposal.Kind)
        {
            case DraftKind.ServiceRecord:
                var section = proposal.ServiceRecord ?? new ServiceRecordProposal();
                yield return $"{ServiceRecordSection}.dateServiced";
                yield return $"{ServiceRecordSection}.odometerReading";
                yield return $"{ServiceRecordSection}.shopOrPerson";
                var lines = DraftProposalMapper.LinesOf(section);
                for (var index = 0; index < lines.Count; index++)
                {
                    var line = lines[index];
                    var prefix = $"{ServiceRecordSection}.lineItems[{index}]";
                    yield return $"{prefix}.description";
                    if (line.FlatCost is not null)
                    {
                        yield return $"{prefix}.flatCost";
                    }

                    if (line.LaborHours is not null || line.LaborRate is not null)
                    {
                        yield return $"{prefix}.laborHours";
                        yield return $"{prefix}.laborRate";
                    }

                    var parts = line.Parts ?? [];
                    for (var part = 0; part < parts.Count; part++)
                    {
                        yield return $"{prefix}.parts[{part}].name";
                        yield return $"{prefix}.parts[{part}].quantity";
                        yield return $"{prefix}.parts[{part}].unitPrice";
                    }
                }

                break;

            case DraftKind.Fuel:
            case DraftKind.Energy:
                yield return $"{FuelEnergySection}.date";
                yield return $"{FuelEnergySection}.unitPrice";
                yield return $"{FuelEnergySection}.quantity";
                if (proposal.FuelEnergy?.TotalCost is not null)
                {
                    yield return $"{FuelEnergySection}.totalCost";
                }

                break;

            case DraftKind.Inspection:
                yield return $"{InspectionSection}.type";
                yield return $"{InspectionSection}.date";
                yield return $"{InspectionSection}.cost";
                if (proposal.Inspection?.NextDueDate is not null)
                {
                    yield return $"{InspectionSection}.nextDueDate";
                }

                break;

            case DraftKind.Registration:
                yield return $"{RegistrationSection}.state";
                yield return $"{RegistrationSection}.cost";
                yield return $"{RegistrationSection}.expirationDate";
                break;

            case DraftKind.Insurance:
                yield return $"{InsuranceSection}.startDate";
                yield return $"{InsuranceSection}.endDate";
                yield return $"{InsuranceSection}.duration";
                yield return $"{InsuranceSection}.cost";
                break;

            case DraftKind.Tax:
                yield return $"{TaxSection}.type";
                yield return $"{TaxSection}.amount";
                yield return $"{TaxSection}.recurrence";
                break;

            default:
                yield return "kind";
                break;
        }
    }

    // Advisory only: warnings never stop a draft from being accepted.
    private async Task<IReadOnlyList<string>> WarningsAsync(DraftProposal proposal, DraftCheckContext context, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        if (proposal.Kind == DraftKind.ServiceRecord)
        {
            var section = proposal.ServiceRecord ?? new ServiceRecordProposal();
            if (DraftProposalMapper.LinesOf(section).Count == 0)
            {
                warnings.Add("No line items were found.");
            }
            else if (section.DocumentTotal is { } documentTotal && DraftProposalMapper.LineItemsTotal(section) is { } lineTotal && lineTotal != documentTotal)
            {
                warnings.Add(
                    $"The line items add up to {lineTotal.ToString("0.00", CultureInfo.InvariantCulture)}, but the document total is {documentTotal.ToString("0.00", CultureInfo.InvariantCulture)}.");
            }
        }

        if (context.DraftId is { } draftId && context.AttachmentSha256 is { Length: > 0 } sha256
            && await duplicates.HasDraftWithSameAttachmentAsync(context.OwnerId, draftId, sha256, cancellationToken))
        {
            warnings.Add("The same file was already received in another draft.");
        }

        if (proposal.Kind is { } kind && context.VehicleId is { } vehicleId
            && DraftProposalMapper.DateAndTotal(proposal) is var (date, total) && date is { } day && total is { } amount
            && await duplicates.HasRecordLikeAsync(context.OwnerId, vehicleId, DraftKinds.ToText(kind), day, amount, cancellationToken))
        {
            warnings.Add("A record with the same date and total already exists on this vehicle.");
        }

        return warnings;
    }
}
