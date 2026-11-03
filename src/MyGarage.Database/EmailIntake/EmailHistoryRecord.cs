namespace MyGarage.Database.EmailIntake;

// One email attributed to the owner. Outcome is awaiting_review, included, discarded, expired, not_applicable, or failed.
// Nothing here carries the sender, subject, or text: history keeps only non-content details (FR-028).
public sealed record EmailHistoryRecord(Guid SubmissionId, DateTimeOffset ArrivedAt, string Outcome, IReadOnlyList<EmailHistoryCreatedRecord> Records);

// A record an included draft created; Kind is the stored draft kind text (service_record, fuel, energy, ...).
public sealed record EmailHistoryCreatedRecord(string Kind, Guid VehicleId, Guid RecordId);
