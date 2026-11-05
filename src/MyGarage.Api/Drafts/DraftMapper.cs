using System.Text.Json;
using MyGarage.Database.Common;
using MyGarage.Database.Drafts;
using MyGarage.Database.EmailIntake;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.EmailIntake;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Drafts;

public static class DraftMapper
{
    // The stored proposal is written by this API, but unreadable text must not break the list: it reads as an empty proposal.
    public static DraftProposal ReadProposal(string? json)
    {
        try
        {
            return DraftProposalJson.Deserialize(json);
        }
        catch (JsonException)
        {
            return new DraftProposal();
        }
    }

    // The draft's own kind column wins when the stored proposal does not name one.
    public static DraftProposal WithKind(DraftProposal proposal, DraftKind? kind) =>
        proposal.Kind is not null || kind is null
            ? proposal
            : new DraftProposal
            {
                Kind = kind,
                ServiceRecord = proposal.ServiceRecord,
                FuelEnergy = proposal.FuelEnergy,
                Inspection = proposal.Inspection,
                Registration = proposal.Registration,
                Insurance = proposal.Insurance,
                Tax = proposal.Tax,
            };

    public static string? VehicleDisplayName(Guid? vehicleId, int? year, string? make, string? model) =>
        vehicleId is null || make is null || model is null ? null : VehicleName.Compose(year, make, model);

    // Anything but the stored text upload reads as an email, the original source.
    public static DraftOrigin OriginFromText(string? text) => text == "upload" ? DraftOrigin.Upload : DraftOrigin.Email;

    public static MaintenanceDraftSummaryDto ToSummaryDto(MaintenanceDraftSummaryRecord record)
    {
        var kind = DraftKinds.FromText(record.Kind);
        return new MaintenanceDraftSummaryDto
        {
            Id = record.Id,
            State = DraftKinds.StateFromText(record.State),
            Kind = kind,
            VehicleName = VehicleDisplayName(record.VehicleId, record.VehicleYear, record.VehicleMake, record.VehicleModel),
            ArrivedAt = record.ArrivedAt,
            ExpiresAt = record.ExpiresAt,
            Origin = OriginFromText(record.Origin),
            HeadlineTotal = DraftProposalMapper.HeadlineTotal(WithKind(ReadProposal(record.Proposal), kind)),
        };
    }

    public static PagedResultDto<MaintenanceDraftSummaryDto> ToPageDto(PagedResult<MaintenanceDraftSummaryRecord> page) =>
        new()
        {
            Items = [.. page.Items.Select(ToSummaryDto)],
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount,
            TotalPages = page.TotalPages,
        };

    // A stored created-record kind this API does not know has no link to show, so it is left out.
    public static EmailHistoryItemDto ToHistoryDto(EmailHistoryRecord record) =>
        new()
        {
            SubmissionId = record.SubmissionId,
            ArrivedAt = record.ArrivedAt,
            Outcome = HistoryOutcomeFromText(record.Outcome),
            Origin = OriginFromText(record.Origin),
            Records =
            [
                .. record.Records
                    .Select(created => (Kind: DraftKinds.FromText(created.Kind), created.VehicleId, created.RecordId))
                    .Where(created => created.Kind is not null)
                    .Select(created => new EmailHistoryRecordDto { Kind = created.Kind!.Value, VehicleId = created.VehicleId, RecordId = created.RecordId }),
            ],
        };

    public static PagedResultDto<EmailHistoryItemDto> ToHistoryPageDto(PagedResult<EmailHistoryRecord> page) =>
        new()
        {
            Items = [.. page.Items.Select(ToHistoryDto)],
            Page = page.Page,
            PageSize = page.PageSize,
            TotalCount = page.TotalCount,
            TotalPages = page.TotalPages,
        };

    private static EmailHistoryOutcome HistoryOutcomeFromText(string text) => text switch
    {
        "awaiting_review" => EmailHistoryOutcome.AwaitingReview,
        "included" => EmailHistoryOutcome.Included,
        "discarded" => EmailHistoryOutcome.Discarded,
        "expired" => EmailHistoryOutcome.Expired,
        "not_applicable" => EmailHistoryOutcome.NotApplicable,
        "failed" => EmailHistoryOutcome.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Unknown stored history outcome."),
    };

    public static MaintenanceDraftDto ToDto(
        MaintenanceDraftDetailRecord record,
        DraftProposal proposal,
        IReadOnlyList<VehicleMatchCandidate> vehicles,
        CompatibilityDto compatibility)
    {
        var match = DraftKinds.MatchFromText(record.VehicleMatch);
        var hasAttachment = record.AttachmentId is not null && record.AttachmentFileName is not null;

        return new MaintenanceDraftDto
        {
            Id = record.Id,
            State = DraftKinds.StateFromText(record.State),
            Kind = DraftKinds.FromText(record.Kind),
            Proposal = proposal,
            Vehicle = new DraftVehicleDto
            {
                VehicleId = record.VehicleId,
                Match = match,
                Candidates =
                [
                    .. vehicles.Select(vehicle => new VehicleSuggestionDto
                    {
                        VehicleId = vehicle.Id,
                        Name = VehicleName.Compose(vehicle.Year, vehicle.Make, vehicle.Model),
                        Match = vehicle.Id == record.VehicleId ? match : VehicleMatch.None,
                    }),
                ],
            },
            Source = hasAttachment
                ? new DraftSourceDto
                {
                    Kind = "attachment",
                    FileName = record.AttachmentFileName,
                    ContentType = record.AttachmentContentType,
                    SizeBytes = record.AttachmentSizeBytes,
                }
                : new DraftSourceDto { Kind = "text" },
            Email = OriginFromText(record.Origin) == DraftOrigin.Upload
                ? new DraftEmailDto { ArrivedAt = record.ArrivedAt }
                : record.Sender is null && record.Subject is null
                    ? null
                    : new DraftEmailDto { ArrivedAt = record.ArrivedAt, Sender = record.Sender, Subject = record.Subject },
            Compatibility = compatibility,
            Origin = OriginFromText(record.Origin),
            Confidence = record.Confidence,
            Notes = record.Notes,
            HasAttachment = hasAttachment,
            ExpiresAt = record.ExpiresAt,
        };
    }
}
