using System.Text.Json;
using Dapper;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.EmailIntake;

// Owns the email_submissions queue and its attachment rows; every statement here is a single transaction.
public sealed class EmailSubmissionRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    // Stores the submission as queued together with its attachment rows. A message id that was already stored is a duplicate and changes nothing,
    // except a failed submission: it is queued again with the new content, its old attachment rows are removed, and their ids come back
    // so the caller deletes the old blobs once this has committed.
    public async Task<AddSubmissionResult> AddReceivedAsync(
        EmailSubmissionRecord submission, IReadOnlyList<EmailAttachmentRecord> attachments, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Taking the lock first makes concurrent resends of one failed message queue up: the first requeues it, the others see it queued.
        var previousId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            scripts.Get("EmailIntake/LockFailed"), new { submission.MessageId }, transaction, cancellationToken: cancellationToken));
        IReadOnlyList<Guid> removed = [];
        if (previousId is { } failedId)
        {
            removed = (await connection.QueryAsync<Guid>(new CommandDefinition(
                scripts.Get("EmailIntake/DeleteAttachmentsOfSubmission"), new { SubmissionId = failedId }, transaction, cancellationToken: cancellationToken))).ToList();
        }

        var stored = await connection.QuerySingleOrDefaultAsync<StoredSubmission>(new CommandDefinition(
            scripts.Get("EmailIntake/InsertSubmission"), submission, transaction, cancellationToken: cancellationToken));
        if (stored is null)
        {
            return await DuplicateAsync(connection, transaction, submission.MessageId, cancellationToken);
        }

        foreach (var attachment in attachments)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                scripts.Get("EmailIntake/InsertAttachment"), attachment, transaction, cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return stored.Inserted
            ? new AddSubmissionResult(AddSubmissionStatus.Added, submission.Id)
            : new AddSubmissionResult(AddSubmissionStatus.Requeued, submission.Id) { PreviousSubmissionId = previousId, RemovedAttachmentIds = removed };
    }

    // Records a message from a sender that matched no single user: no sender, subject, text, or attachments are kept.
    public async Task<AddSubmissionResult> AddNotAcceptedAsync(Guid id, string messageId, DateTimeOffset arrivedAt, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var inserted = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            scripts.Get("EmailIntake/InsertNotAccepted"), new { Id = id, MessageId = messageId, ArrivedAt = arrivedAt }, transaction, cancellationToken: cancellationToken));
        if (inserted is null)
        {
            return await DuplicateAsync(connection, transaction, messageId, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new AddSubmissionResult(AddSubmissionStatus.Added, id);
    }

    // Leases the oldest due submission (queued and due, or processing with an expired lease) and counts the attempt; null when none is due.
    public async Task<EmailSubmissionRecord?> ClaimNextAsync(DateTimeOffset now, TimeSpan leaseFor, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<EmailSubmissionRecord>(new CommandDefinition(
            scripts.Get("EmailIntake/ClaimNext"), new { Now = now, LockedUntil = now + leaseFor }, cancellationToken: cancellationToken));
    }

    // Final status ready or not_applicable; not_applicable also purges the email content.
    public async Task CompleteAsync(Guid id, string status, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("EmailIntake/Complete"), new { Id = id, Status = status, Now = now }, cancellationToken: cancellationToken));
    }

    // Back to the queue; reasonCode is a short code, never email content.
    public async Task RetryAsync(Guid id, DateTimeOffset nextAttemptAt, string reasonCode, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("EmailIntake/Retry"), new { Id = id, NextAttemptAt = nextAttemptAt, Reason = reasonCode }, cancellationToken: cancellationToken));
    }

    public async Task FailAsync(Guid id, string reasonCode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("EmailIntake/Fail"), new { Id = id, Reason = reasonCode, Now = now }, cancellationToken: cancellationToken));
    }

    // Ownership is proved in SQL: another owner's id yields nothing.
    public async Task<IReadOnlyList<EmailAttachmentRecord>> ListAttachmentsAsync(Guid submissionId, string ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<EmailAttachmentRecord>(new CommandDefinition(
            scripts.Get("EmailIntake/ListAttachments"), new { SubmissionId = submissionId, OwnerId = ownerId }, cancellationToken: cancellationToken))).ToList();
    }

    // The owner's emails newest first (never not_accepted ones, which have no owner); a page past the last is empty.
    // outcome (awaiting_review, included, discarded, expired, not_applicable, failed) keeps only that outcome.
    public async Task<PagedResult<EmailHistoryRecord>> ListHistoryPageAsync(string ownerId, int page, int pageSize, CancellationToken cancellationToken, string? outcome = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<HistoryRow>(new CommandDefinition(
            scripts.Get("EmailIntake/ListHistoryPage"),
            new { OwnerId = ownerId, PageSize = pageSize, Offset = (page - 1) * pageSize, Outcome = outcome },
            cancellationToken: cancellationToken));
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            scripts.Get("EmailIntake/CountHistory"), new { OwnerId = ownerId, Outcome = outcome }, cancellationToken: cancellationToken));

        var items = rows
            .Select(row => new EmailHistoryRecord(
                row.SubmissionId,
                row.ArrivedAt,
                row.Outcome,
                JsonSerializer.Deserialize<List<EmailHistoryCreatedRecord>>(row.RecordsJson, JsonSerializerOptions.Web) ?? [],
                row.Origin))
            .ToList();
        return new PagedResult<EmailHistoryRecord>(items, page, pageSize, total);
    }

    private sealed class HistoryRow
    {
        public Guid SubmissionId { get; init; }

        public DateTimeOffset ArrivedAt { get; init; }

        public string Outcome { get; init; } = string.Empty;

        public string Origin { get; init; } = "email";

        public string RecordsJson { get; init; } = "[]";
    }

    // For the orphan sweep: true while a row still points at the stored file.
    public async Task<bool> AttachmentExistsAsync(Guid submissionId, Guid attachmentId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            scripts.Get("EmailIntake/AttachmentExists"), new { SubmissionId = submissionId, AttachmentId = attachmentId }, cancellationToken: cancellationToken));
    }

    private sealed class StoredSubmission
    {
        public Guid Id { get; init; }

        // False when the statement updated a failed submission instead of inserting a new one.
        public bool Inserted { get; init; }
    }

    private async Task<AddSubmissionResult> DuplicateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string messageId, CancellationToken cancellationToken)
    {
        var existing = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            scripts.Get("EmailIntake/GetIdByMessageId"), new { MessageId = messageId }, transaction, cancellationToken: cancellationToken));
        return new AddSubmissionResult(AddSubmissionStatus.Duplicate, existing);
    }
}
