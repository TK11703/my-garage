using MyGarage.Database.ServiceRecords;

namespace MyGarage.Database.Drafts;

// The validated values of the record an accepted draft becomes. The Api maps its proposal onto these; this project never sees the proposal.
public abstract record AcceptedRecordValues
{
    // service_record, fuel, energy, inspection, registration, insurance, or tax.
    public abstract string DraftKind { get; }
}

public sealed record ServiceRecordValues(
    DateOnly DateServiced,
    int OdometerReading,
    string? ShopOrPerson,
    IReadOnlyList<ServiceLineItemInput> LineItems,
    decimal? TaxAmount = null) : AcceptedRecordValues
{
    public override string DraftKind => "service_record";
}

// Kind is the stored text "fuel" or "energy"; it is also the draft kind.
public sealed record FuelEnergyValues(string Kind, DateOnly EntryDate, decimal UnitPrice, decimal Quantity, decimal TotalCost) : AcceptedRecordValues
{
    public override string DraftKind => Kind;
}

public sealed record InspectionValues(string InspectionType, DateOnly InspectionDate, decimal Cost, DateOnly? NextDueDate) : AcceptedRecordValues
{
    public override string DraftKind => "inspection";
}

public sealed record RegistrationValues(string State, decimal Cost, DateOnly ExpirationDate) : AcceptedRecordValues
{
    public override string DraftKind => "registration";
}

public sealed record InsuranceValues(DateOnly StartDate, DateOnly EndDate, string Duration, decimal Cost) : AcceptedRecordValues
{
    public override string DraftKind => "insurance";
}

public sealed record TaxValues(string TaxType, decimal Amount, string Recurrence) : AcceptedRecordValues
{
    public override string DraftKind => "tax";
}

public enum AcceptDraftOutcome
{
    Accepted,
    NotFound,
    AlreadyDecided,
    VehicleNotFound,
}

// Attachment is the draft's stored file (when it has one) so the caller can delete the intake blob after the commit.
public sealed record AcceptDraftResult(
    AcceptDraftOutcome Outcome, Guid? RecordId = null, Guid? DocumentId = null, DraftAttachmentRef? Attachment = null);
