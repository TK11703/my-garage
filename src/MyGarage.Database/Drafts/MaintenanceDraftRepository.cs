using Dapper;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.Drafts;

// Creates drafts (with their notifications) for the submission's owner and serves the owner's review list. Every statement is scoped by owner id.
public sealed class MaintenanceDraftRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    // Unreviewed drafts expire this long after creation.
    public static readonly TimeSpan DraftLifetime = TimeSpan.FromDays(30);

    // Inserts every draft and one draft_ready notification per draft, and marks the submission ready, in one transaction.
    // Returns false (and writes nothing) when the submission does not belong to the owner.
    public async Task<bool> CreateDraftsAsync(
        Guid submissionId, string ownerId, IReadOnlyList<NewMaintenanceDraft> drafts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var ready = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            scripts.Get("Drafts/MarkSubmissionReady"), new { SubmissionId = submissionId, OwnerId = ownerId }, transaction, cancellationToken: cancellationToken));
        if (ready is null)
        {
            return false;
        }

        foreach (var draft in drafts)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                scripts.Get("Drafts/InsertDraft"),
                new
                {
                    draft.Id,
                    OwnerId = ownerId,
                    SubmissionId = submissionId,
                    draft.AttachmentId,
                    draft.State,
                    draft.Kind,
                    draft.VehicleId,
                    draft.VehicleMatch,
                    draft.Confidence,
                    Proposal = draft.ProposalJson,
                    draft.Notes,
                    CreatedAt = now,
                    ExpiresAt = now + DraftLifetime,
                },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                scripts.Get("Notifications/Insert"),
                new { Id = Guid.CreateVersion7(), OwnerId = ownerId, DraftId = draft.Id, Title = NotificationTitle(draft.Kind), CreatedAt = now },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    // Only the owner's own vehicles, for matching in code (the AI model never sees this list).
    public async Task<IReadOnlyList<VehicleMatchCandidate>> ListOwnerVehiclesForMatchingAsync(string ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<VehicleMatchCandidate>(new CommandDefinition(
            scripts.Get("Drafts/ListOwnerVehicles"), new { OwnerId = ownerId }, cancellationToken: cancellationToken))).ToList();
    }

    // The owner's drafts awaiting review (needs_review and manual_entry), newest first; a page past the last is empty.
    public async Task<PagedResult<MaintenanceDraftSummaryRecord>> ListOpenPageAsync(
        string ownerId, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var items = (await connection.QueryAsync<MaintenanceDraftSummaryRecord>(new CommandDefinition(
            scripts.Get("Drafts/ListOpenPage"),
            new { OwnerId = ownerId, PageSize = pageSize, Offset = (page - 1) * pageSize },
            cancellationToken: cancellationToken))).ToList();
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            scripts.Get("Drafts/CountOpen"), new { OwnerId = ownerId }, cancellationToken: cancellationToken));
        return new PagedResult<MaintenanceDraftSummaryRecord>(items, page, pageSize, total);
    }

    // Null when the draft does not exist for that owner.
    public async Task<MaintenanceDraftDetailRecord?> GetAsync(string ownerId, Guid draftId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<MaintenanceDraftDetailRecord>(new CommandDefinition(
            scripts.Get("Drafts/GetById"), new { DraftId = draftId, OwnerId = ownerId }, cancellationToken: cancellationToken));
    }

    // False when the draft is not the owner's or is already decided; a manual_entry draft becomes needs_review.
    public async Task<bool> ChangeKindAsync(string ownerId, Guid draftId, string kind, string proposalJson, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            scripts.Get("Drafts/ChangeKind"),
            new { DraftId = draftId, OwnerId = ownerId, Kind = kind, Proposal = proposalJson },
            cancellationToken: cancellationToken)) is not null;
    }

    // The decision, the notification, and the email content change together; the caller then deletes the returned attachment's file.
    public async Task<DiscardDraftResult> DiscardAsync(string ownerId, Guid draftId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var decided = await connection.QuerySingleOrDefaultAsync<DecidedDraft>(new CommandDefinition(
            scripts.Get("Drafts/Decide"),
            new { DraftId = draftId, OwnerId = ownerId, NewState = "discarded", Now = now, VehicleId = (Guid?)null, CreatedRecordKind = (string?)null, CreatedRecordId = (Guid?)null },
            transaction,
            cancellationToken: cancellationToken));
        if (decided is null)
        {
            var state = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                scripts.Get("Drafts/GetState"), new { DraftId = draftId, OwnerId = ownerId }, transaction, cancellationToken: cancellationToken));
            return new DiscardDraftResult(state is null ? DraftDecisionOutcome.NotFound : DraftDecisionOutcome.AlreadyDecided);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Notifications/DeleteForDraft"), new { DraftId = draftId, OwnerId = ownerId }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Drafts/PurgeSubmissionContent"), new { SubmissionId = decided.SubmissionId, OwnerId = ownerId, Now = now }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return new DiscardDraftResult(
            DraftDecisionOutcome.Done,
            decided.AttachmentId is { } attachmentId ? new DraftAttachmentRef(decided.SubmissionId, attachmentId) : null);
    }

    // Expires every undecided draft that is due, for all owners; returns the stored attachments whose files must now be deleted.
    public async Task<ExpireDueResult> ExpireDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var expired = (await connection.QueryAsync<ExpiredDraft>(new CommandDefinition(
            scripts.Get("Drafts/ExpireDue"), new { Now = now }, transaction, cancellationToken: cancellationToken))).ToList();

        foreach (var draft in expired)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                scripts.Get("Notifications/DeleteForDraft"), new { DraftId = draft.Id, draft.OwnerId }, transaction, cancellationToken: cancellationToken));
        }

        foreach (var submissionId in expired.Select(draft => draft.SubmissionId).Distinct())
        {
            await connection.ExecuteAsync(new CommandDefinition(
                scripts.Get("Drafts/PurgeSubmissionContent"), new { SubmissionId = submissionId, OwnerId = (string?)null, Now = now }, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return new ExpireDueResult(
            expired.Count,
            [.. expired.Where(draft => draft.AttachmentId is not null).Select(draft => new DraftAttachmentRef(draft.SubmissionId, draft.AttachmentId!.Value))]);
    }

    // Another draft of the owner, not discarded, that came from a file with the same SHA-256.
    public async Task<bool> HasDraftWithSameAttachmentAsync(string ownerId, Guid draftId, string sha256, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            scripts.Get("Drafts/FindDuplicateByHash"), new { OwnerId = ownerId, DraftId = draftId, Sha256 = sha256 }, cancellationToken: cancellationToken));
    }

    // A record of the owner's vehicle of that kind with the same date and total.
    public async Task<bool> HasRecordLikeAsync(
        string ownerId, Guid vehicleId, string kind, DateOnly date, decimal total, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            scripts.Get("Drafts/FindDuplicateRecord"),
            new { OwnerId = ownerId, VehicleId = vehicleId, Kind = kind, Date = date, Total = total },
            cancellationToken: cancellationToken));
    }

    private sealed class ExpiredDraft
    {
        public Guid Id { get; init; }

        public string OwnerId { get; init; } = string.Empty;

        public Guid SubmissionId { get; init; }

        public Guid? AttachmentId { get; init; }
    }

    // Generic text only: never the sender, subject, or any email content.
    internal static string NotificationTitle(string? kind) => kind switch
    {
        "service_record" => "New draft service record ready to review",
        "fuel" => "New draft fuel entry ready to review",
        "energy" => "New draft charging entry ready to review",
        "inspection" => "New draft inspection ready to review",
        "registration" => "New draft registration ready to review",
        "insurance" => "New draft insurance policy ready to review",
        "tax" => "New draft tax ready to review",
        _ => "New draft maintenance item ready to review",
    };
}
