using MyGarage.Api.EmailIntake;
using MyGarage.Database.Common;
using MyGarage.Database.Drafts;
using MyGarage.Database.EmailIntake;
using MyGarage.Database.Notifications;
using MyGarage.Database.VehicleDocuments;
using MyGarage.Database.Vehicles;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Drafts;

public enum DraftOutcome
{
    Ok,
    NotFound,
    Conflict,
}

public sealed record DraftResult<T>(DraftOutcome Outcome, T? Value = default);

// A draft's original file opened for streaming; dispose it to release the storage connection.
public sealed class DraftAttachmentContent(string contentType, Stream content) : IAsyncDisposable
{
    public string ContentType { get; } = contentType;

    public Stream Content { get; } = content;

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

// The review workflow. Every read and write is scoped to the caller's owner id; files are only touched after the owner-scoped statement passed.
// Logs carry ids only: never email text, extracted values, or file names.
public sealed class DraftService(
    MaintenanceDraftRepository drafts,
    DraftAcceptanceRepository acceptance,
    NotificationRepository notifications,
    VehicleRepository vehicles,
    CompatibilityChecker checker,
    IIntakeBlobStore intake,
    IVehicleDocumentBlobStore documents,
    TimeProvider timeProvider,
    ILogger<DraftService> logger)
{
    private const int DefaultPageSize = 10;

    public async Task<PagedResultDto<MaintenanceDraftSummaryDto>> ListAsync(string ownerId, int page, int? pageSize, CancellationToken cancellationToken)
    {
        var size = pageSize ?? DefaultPageSize;
        var result = await drafts.ListOpenPageAsync(ownerId, page, size, cancellationToken);

        // A page beyond the last is answered with the last page; the returned Page is the one served.
        var effectivePage = Math.Max(1, Math.Min(page, result.TotalPages));
        if (effectivePage != page)
        {
            result = await drafts.ListOpenPageAsync(ownerId, effectivePage, size, cancellationToken);
        }

        return DraftMapper.ToPageDto(result);
    }

    // Opening a draft reads its notification.
    public async Task<MaintenanceDraftDto?> GetAsync(string ownerId, Guid draftId, CancellationToken cancellationToken)
    {
        var detail = await drafts.GetAsync(ownerId, draftId, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        await notifications.MarkReadForDraftAsync(ownerId, draftId, timeProvider.GetUtcNow(), cancellationToken);
        return await BuildAsync(ownerId, detail, cancellationToken);
    }

    // Null when the draft is not the caller's.
    public async Task<CompatibilityDto?> CheckCompatibilityAsync(string ownerId, Guid draftId, DraftProposal proposal, CancellationToken cancellationToken)
    {
        var detail = await drafts.GetAsync(ownerId, draftId, cancellationToken);
        return detail is null
            ? null
            : await checker.CheckAsync(proposal, new DraftCheckContext(ownerId, draftId, detail.VehicleId, detail.AttachmentSha256), cancellationToken);
    }

    public async Task<DraftResult<MaintenanceDraftDto>> ChangeKindAsync(string ownerId, Guid draftId, DraftKind kind, CancellationToken cancellationToken)
    {
        var detail = await drafts.GetAsync(ownerId, draftId, cancellationToken);
        if (detail is null)
        {
            return new DraftResult<MaintenanceDraftDto>(DraftOutcome.NotFound);
        }

        if (!DraftKinds.IsOpen(detail.State))
        {
            return new DraftResult<MaintenanceDraftDto>(DraftOutcome.Conflict);
        }

        var current = DraftMapper.WithKind(DraftMapper.ReadProposal(detail.Proposal), DraftKinds.FromText(detail.Kind));
        var changed = DraftKindChanger.Change(current, kind);
        if (!await drafts.ChangeKindAsync(ownerId, draftId, DraftKinds.ToText(kind), DraftProposalJson.Serialize(changed), cancellationToken))
        {
            return new DraftResult<MaintenanceDraftDto>(DraftOutcome.Conflict);
        }

        var updated = await drafts.GetAsync(ownerId, draftId, cancellationToken);
        return updated is null
            ? new DraftResult<MaintenanceDraftDto>(DraftOutcome.NotFound)
            : new DraftResult<MaintenanceDraftDto>(DraftOutcome.Ok, await BuildAsync(ownerId, updated, cancellationToken));
    }

    // The record, the draft state, and the optional kept document commit together (research R10 orders the files around that commit).
    public async Task<DraftResult<AcceptedDraftDto>> AcceptAsync(string ownerId, Guid draftId, AcceptDraftRequest request, CancellationToken cancellationToken)
    {
        var detail = await drafts.GetAsync(ownerId, draftId, cancellationToken);
        if (detail is null)
        {
            return new DraftResult<AcceptedDraftDto>(DraftOutcome.NotFound);
        }

        if (!DraftKinds.IsOpen(detail.State))
        {
            return new DraftResult<AcceptedDraftDto>(DraftOutcome.Conflict);
        }

        // The validator has already guaranteed the vehicle id, the kind, and the section values.
        var proposal = request.Proposal!;
        var kind = proposal.Kind!.Value;
        var vehicleId = request.VehicleId!.Value;
        var values = DraftProposalMapper.ToValues(proposal);

        // Ownership of the vehicle is proved before any file is written under its id.
        if (await vehicles.GetByIdAsync(ownerId, vehicleId, cancellationToken) is null)
        {
            return new DraftResult<AcceptedDraftDto>(DraftOutcome.NotFound);
        }

        Guid? documentId = null;
        if (request.KeepOriginal && detail.AttachmentId is { } attachmentId)
        {
            var newDocumentId = Guid.CreateVersion7();
            if (await documents.CopyFromIntakeAsync(detail.SubmissionId, attachmentId, vehicleId, newDocumentId, cancellationToken))
            {
                documentId = newDocumentId;
            }
        }

        AcceptDraftResult result;
        try
        {
            result = await acceptance.AcceptAsync(ownerId, draftId, vehicleId, values, documentId, timeProvider.GetUtcNow(), cancellationToken);
        }
        catch
        {
            await TryDeleteDocumentAsync(vehicleId, documentId);
            throw;
        }

        if (result.Outcome != AcceptDraftOutcome.Accepted)
        {
            await TryDeleteDocumentAsync(vehicleId, documentId);
            return new DraftResult<AcceptedDraftDto>(result.Outcome == AcceptDraftOutcome.AlreadyDecided ? DraftOutcome.Conflict : DraftOutcome.NotFound);
        }

        if (result.Attachment is { } stored)
        {
            await TryDeleteIntakeAsync(stored);
        }

        EmailIntakeMetrics.DraftsDecided.Add(1, new KeyValuePair<string, object?>("decision", "included"));
        logger.LogInformation("Draft {DraftId} accepted as {Kind}", draftId, DraftKinds.ToText(kind));
        return new DraftResult<AcceptedDraftDto>(
            DraftOutcome.Ok,
            new AcceptedDraftDto { Kind = kind, VehicleId = vehicleId, RecordId = result.RecordId!.Value, DocumentId = result.DocumentId });
    }

    public async Task<DraftOutcome> DiscardAsync(string ownerId, Guid draftId, CancellationToken cancellationToken)
    {
        var result = await drafts.DiscardAsync(ownerId, draftId, timeProvider.GetUtcNow(), cancellationToken);
        switch (result.Outcome)
        {
            case DraftDecisionOutcome.NotFound:
                return DraftOutcome.NotFound;
            case DraftDecisionOutcome.AlreadyDecided:
                return DraftOutcome.Conflict;
        }

        if (result.Attachment is { } stored)
        {
            await TryDeleteIntakeAsync(stored);
        }

        EmailIntakeMetrics.DraftsDecided.Add(1, new KeyValuePair<string, object?>("decision", "discarded"));
        logger.LogInformation("Draft {DraftId} discarded", draftId);
        return DraftOutcome.Ok;
    }

    // Null when the draft is not the caller's, has no stored file, or the file is gone; the owner-scoped lookup always comes first.
    public async Task<DraftAttachmentContent?> OpenAttachmentAsync(string ownerId, Guid draftId, CancellationToken cancellationToken)
    {
        var detail = await drafts.GetAsync(ownerId, draftId, cancellationToken);
        if (detail?.AttachmentId is not { } attachmentId || detail.AttachmentContentType is not { } contentType)
        {
            return null;
        }

        var stream = await intake.OpenReadAsync(detail.SubmissionId, attachmentId, cancellationToken);
        return stream is null ? null : new DraftAttachmentContent(contentType, stream);
    }

    private async Task<MaintenanceDraftDto> BuildAsync(string ownerId, MaintenanceDraftDetailRecord detail, CancellationToken cancellationToken)
    {
        var kind = DraftKinds.FromText(detail.Kind);
        var proposal = DraftMapper.WithKind(DraftMapper.ReadProposal(detail.Proposal), kind);
        var candidates = await drafts.ListOwnerVehiclesForMatchingAsync(ownerId, cancellationToken);

        // A decided or expired draft keeps no proposal, so there is nothing left to check.
        var compatibility = DraftKinds.IsOpen(detail.State)
            ? await checker.CheckAsync(proposal, new DraftCheckContext(ownerId, detail.Id, detail.VehicleId, detail.AttachmentSha256), cancellationToken)
            : new CompatibilityDto { Kind = kind, IsAcceptable = false, Fields = [] };

        return DraftMapper.ToDto(detail, proposal, candidates, compatibility);
    }

    private async Task TryDeleteIntakeAsync(DraftAttachmentRef stored)
    {
        try
        {
            await intake.DeleteAsync(stored.SubmissionId, stored.AttachmentId, CancellationToken.None);
        }
        catch (IntakeStorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not delete the stored file {AttachmentId} of submission {SubmissionId}", stored.AttachmentId, stored.SubmissionId);
        }
    }

    private async Task TryDeleteDocumentAsync(Guid vehicleId, Guid? documentId)
    {
        if (documentId is not { } id)
        {
            return;
        }

        try
        {
            await documents.DeleteAsync(vehicleId, id, CancellationToken.None);
        }
        catch (DocumentStorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not delete the copied document {DocumentId} of vehicle {VehicleId}", id, vehicleId);
        }
    }
}
