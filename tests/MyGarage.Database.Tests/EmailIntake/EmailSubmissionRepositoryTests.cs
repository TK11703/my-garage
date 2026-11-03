using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.EmailIntake;
using MyGarage.Database.Tests.Common;
using Npgsql;

namespace MyGarage.Database.Tests.EmailIntake;

[Collection(DatabaseCollection.Name)]
public class EmailSubmissionRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private async Task<(EmailSubmissionRepository Repository, NpgsqlDataSource DataSource)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (new EmailSubmissionRepository(dataSource, new SqlScriptStore()), dataSource);
    }

    private static EmailSubmissionRecord Submission(string messageId = "<m1@mail.test>", string ownerId = "owner-a") =>
        new()
        {
            Id = Guid.CreateVersion7(),
            MessageId = messageId,
            ArrivedAt = T0,
            ReceivedAt = T0.AddMinutes(-1),
            OwnerId = ownerId,
            Sender = "alice@example.com",
            Subject = "Fwd: invoice",
            BodyText = "Oil change today",
            SkippedAttachments = """[{"fileName":"notes.txt","reason":"unsupported_type"}]""",
        };

    private static EmailAttachmentRecord Attachment(Guid submissionId, string contentType = "application/pdf", string fileName = "invoice.pdf") =>
        new()
        {
            Id = Guid.CreateVersion7(),
            SubmissionId = submissionId,
            OwnerId = "owner-a",
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = 2048,
            Sha256 = new string('b', 64),
            CreatedAt = T0,
        };

    private sealed class SubmissionRow
    {
        public string MessageId { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public DateTimeOffset ArrivedAt { get; init; }

        public DateTimeOffset? ReceivedAt { get; init; }

        public string? OwnerId { get; init; }

        public string? Sender { get; init; }

        public string? Subject { get; init; }

        public string? BodyText { get; init; }

        public int AttemptCount { get; init; }

        public DateTimeOffset NextAttemptAt { get; init; }

        public string SkippedAttachments { get; init; } = string.Empty;
    }

    private static async Task<SubmissionRow> ReadSubmissionAsync(NpgsqlDataSource dataSource, Guid id)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<SubmissionRow>(
            "SELECT message_id AS MessageId, status, arrived_at AS ArrivedAt, received_at AS ReceivedAt, owner_id AS OwnerId, sender, subject, body_text AS BodyText, " +
            "attempt_count AS AttemptCount, next_attempt_at AS NextAttemptAt, skipped_attachments::text AS SkippedAttachments FROM email_submissions WHERE id = @id",
            new { id });
    }

    private static async Task<long> CountAsync(NpgsqlDataSource dataSource, string table)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<long>($"SELECT count(*) FROM {table}");
    }

    [Fact]
    public async Task AddReceived_StoresAQueuedSubmissionWithItsAttachments()
    {
        var (repository, dataSource) = await CreateAsync();
        var submission = Submission();
        var attachments = new[] { Attachment(submission.Id), Attachment(submission.Id, "image/png", "photo.png") };

        var result = await repository.AddReceivedAsync(submission, attachments, CancellationToken.None);

        Assert.Equal(AddSubmissionStatus.Added, result.Status);
        Assert.Equal(submission.Id, result.SubmissionId);
        var row = await ReadSubmissionAsync(dataSource, submission.Id);
        Assert.Equal("queued", row.Status);
        Assert.Equal("<m1@mail.test>", row.MessageId);
        Assert.Equal(T0, row.ArrivedAt);
        Assert.Equal(T0.AddMinutes(-1), row.ReceivedAt);
        Assert.Equal("owner-a", row.OwnerId);
        Assert.Equal("alice@example.com", row.Sender);
        Assert.Equal("Fwd: invoice", row.Subject);
        Assert.Equal("Oil change today", row.BodyText);
        Assert.Equal(0, row.AttemptCount);
        Assert.Equal(T0, row.NextAttemptAt);
        Assert.Contains("unsupported_type", row.SkippedAttachments);
        await using var connection = await dataSource.OpenConnectionAsync();
        var stored = (await connection.QueryAsync<(Guid Id, string ContentType, int SizeBytes, string Sha256)>(
            "SELECT id, content_type, size_bytes, trim(sha256) FROM email_attachments WHERE submission_id = @id ORDER BY file_name", new { id = submission.Id })).ToList();
        Assert.Equal(attachments.OrderBy(a => a.FileName).Select(a => a.Id), stored.Select(s => s.Id));
        Assert.All(stored, s => Assert.Equal(2048, s.SizeBytes));
    }

    [Fact]
    public async Task AddReceived_SameMessageIdAgain_ReturnsDuplicateWithTheExistingIdAndWritesNothing()
    {
        var (repository, dataSource) = await CreateAsync();
        var first = Submission();
        await repository.AddReceivedAsync(first, [Attachment(first.Id)], CancellationToken.None);
        var second = Submission();

        var result = await repository.AddReceivedAsync(second, [Attachment(second.Id)], CancellationToken.None);

        Assert.Equal(AddSubmissionStatus.Duplicate, result.Status);
        Assert.Equal(first.Id, result.SubmissionId);
        Assert.Equal(1, await CountAsync(dataSource, "email_submissions"));
        Assert.Equal(1, await CountAsync(dataSource, "email_attachments"));
    }

    [Fact]
    public async Task AddReceived_AFailingAttachment_RollsBackTheSubmission()
    {
        var (repository, dataSource) = await CreateAsync();
        var submission = Submission();

        await Assert.ThrowsAsync<PostgresException>(() =>
            repository.AddReceivedAsync(submission, [Attachment(submission.Id), Attachment(submission.Id, "text/plain", "bad.txt")], CancellationToken.None));

        Assert.Equal(0, await CountAsync(dataSource, "email_submissions"));
        Assert.Equal(0, await CountAsync(dataSource, "email_attachments"));
    }

    [Fact]
    public async Task AddNotAccepted_StoresOnlyTheMessageIdAndTimes()
    {
        var (repository, dataSource) = await CreateAsync();
        var id = Guid.CreateVersion7();

        var result = await repository.AddNotAcceptedAsync(id, "<m2@mail.test>", T0, CancellationToken.None);

        Assert.Equal(AddSubmissionStatus.Added, result.Status);
        Assert.Equal(id, result.SubmissionId);
        var row = await ReadSubmissionAsync(dataSource, id);
        Assert.Equal("not_accepted", row.Status);
        Assert.Equal("<m2@mail.test>", row.MessageId);
        Assert.Equal(T0, row.ArrivedAt);
        Assert.Null(row.OwnerId);
        Assert.Null(row.Sender);
        Assert.Null(row.Subject);
        Assert.Null(row.BodyText);
        Assert.Null(row.ReceivedAt);
        Assert.Equal("[]", row.SkippedAttachments);
        Assert.Equal(0, await CountAsync(dataSource, "email_attachments"));
    }

    [Fact]
    public async Task AddNotAccepted_DuplicateMessageId_ReturnsDuplicate()
    {
        var (repository, dataSource) = await CreateAsync();
        var first = Guid.CreateVersion7();
        await repository.AddNotAcceptedAsync(first, "<m3@mail.test>", T0, CancellationToken.None);

        var result = await repository.AddNotAcceptedAsync(Guid.CreateVersion7(), "<m3@mail.test>", T0.AddMinutes(1), CancellationToken.None);

        Assert.Equal(AddSubmissionStatus.Duplicate, result.Status);
        Assert.Equal(first, result.SubmissionId);
        Assert.Equal(1, await CountAsync(dataSource, "email_submissions"));
    }

    [Fact]
    public async Task AddNotAccepted_ThenAReceivedWithTheSameMessageId_IsADuplicate()
    {
        var (repository, _) = await CreateAsync();
        var first = Guid.CreateVersion7();
        await repository.AddNotAcceptedAsync(first, "<m1@mail.test>", T0, CancellationToken.None);

        var result = await repository.AddReceivedAsync(Submission(), [], CancellationToken.None);

        Assert.Equal(AddSubmissionStatus.Duplicate, result.Status);
        Assert.Equal(first, result.SubmissionId);
    }

    private static async Task SetStatusAsync(NpgsqlDataSource dataSource, Guid id, string status)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "UPDATE email_submissions SET status = @status, attempt_count = 3, failure_reason = 'extraction_failed', locked_until = NULL, " +
            "sender = NULL, subject = NULL, body_text = NULL, skipped_attachments = '[]'::jsonb, content_purged_at = now() WHERE id = @id",
            new { id, status });
    }

    [Fact]
    public async Task AddReceived_FailedSubmission_IsRequeuedWithTheNewContentAndAttachments()
    {
        var (repository, dataSource) = await CreateAsync();
        var first = Submission();
        var oldAttachments = new[] { Attachment(first.Id), Attachment(first.Id, "image/png", "photo.png") };
        await repository.AddReceivedAsync(first, oldAttachments, CancellationToken.None);
        await SetStatusAsync(dataSource, first.Id, "failed");
        var resend = new EmailSubmissionRecord
        {
            Id = Guid.CreateVersion7(),
            MessageId = first.MessageId,
            ArrivedAt = T0.AddHours(2),
            ReceivedAt = T0.AddHours(2),
            OwnerId = "owner-a",
            Sender = "alice@example.com",
            Subject = "Fwd: invoice again",
            BodyText = "New body",
            SkippedAttachments = "[]",
        };
        var newAttachment = Attachment(resend.Id, "image/png", "new.png");

        var result = await repository.AddReceivedAsync(resend, [newAttachment], CancellationToken.None);

        Assert.Equal(AddSubmissionStatus.Requeued, result.Status);
        Assert.Equal(resend.Id, result.SubmissionId);
        Assert.Equal(first.Id, result.PreviousSubmissionId);
        Assert.Equal(oldAttachments.Select(a => a.Id).Order(), result.RemovedAttachmentIds.Order());
        Assert.Equal(1, await CountAsync(dataSource, "email_submissions"));
        var row = await ReadSubmissionAsync(dataSource, resend.Id);
        Assert.Equal("queued", row.Status);
        Assert.Equal(0, row.AttemptCount);
        Assert.Equal(T0.AddHours(2), row.NextAttemptAt);
        Assert.Equal("New body", row.BodyText);
        Assert.Equal("Fwd: invoice again", row.Subject);
        Assert.Equal("alice@example.com", row.Sender);
        Assert.Equal("[]", row.SkippedAttachments);
        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal(
            [newAttachment.Id],
            (await connection.QueryAsync<Guid>("SELECT id FROM email_attachments WHERE submission_id = @id", new { id = resend.Id })).ToList());
        Assert.Equal(1, await CountAsync(dataSource, "email_attachments"));
        Assert.Null(await connection.ExecuteScalarAsync<string?>("SELECT failure_reason FROM email_submissions WHERE id = @id", new { id = resend.Id }));
        Assert.Null(await connection.ExecuteScalarAsync<DateTimeOffset?>("SELECT locked_until FROM email_submissions WHERE id = @id", new { id = resend.Id }));
    }

    [Fact]
    public async Task AddReceived_FailedSubmissionWithoutAttachments_IsRequeuedAndReturnsNoRemovedIds()
    {
        var (repository, dataSource) = await CreateAsync();
        var first = Submission();
        await repository.AddReceivedAsync(first, [], CancellationToken.None);
        await SetStatusAsync(dataSource, first.Id, "failed");

        var result = await repository.AddReceivedAsync(Submission(), [], CancellationToken.None);

        Assert.Equal(AddSubmissionStatus.Requeued, result.Status);
        Assert.Empty(result.RemovedAttachmentIds);
        Assert.Equal(1, await CountAsync(dataSource, "email_submissions"));
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("processing")]
    [InlineData("ready")]
    [InlineData("not_applicable")]
    public async Task AddReceived_SubmissionThatIsNotFailed_IsADuplicateAndChangesNothing(string status)
    {
        var (repository, dataSource) = await CreateAsync();
        var first = Submission();
        var attachment = Attachment(first.Id);
        await repository.AddReceivedAsync(first, [attachment], CancellationToken.None);
        await SetStatusAsync(dataSource, first.Id, status);
        var before = await ReadSubmissionAsync(dataSource, first.Id);
        var second = Submission();

        var result = await repository.AddReceivedAsync(second, [Attachment(second.Id, "image/png", "other.png")], CancellationToken.None);

        Assert.Equal(AddSubmissionStatus.Duplicate, result.Status);
        Assert.Equal(first.Id, result.SubmissionId);
        Assert.Empty(result.RemovedAttachmentIds);
        var after = await ReadSubmissionAsync(dataSource, first.Id);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.AttemptCount, after.AttemptCount);
        Assert.Equal(before.BodyText, after.BodyText);
        Assert.Equal(before.ArrivedAt, after.ArrivedAt);
        Assert.Equal(1, await CountAsync(dataSource, "email_submissions"));
        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal(
            [attachment.Id],
            (await connection.QueryAsync<Guid>("SELECT id FROM email_attachments")).ToList());
    }

    [Fact]
    public async Task AddReceived_NotAcceptedSubmission_IsNeverRequeued()
    {
        var (repository, dataSource) = await CreateAsync();
        var first = Guid.CreateVersion7();
        await repository.AddNotAcceptedAsync(first, "<m1@mail.test>", T0, CancellationToken.None);

        var result = await repository.AddReceivedAsync(Submission(), [], CancellationToken.None);

        Assert.Equal(AddSubmissionStatus.Duplicate, result.Status);
        Assert.Equal("not_accepted", (await ReadSubmissionAsync(dataSource, first)).Status);
    }

    [Fact]
    public async Task AddReceived_ConcurrentCallsForOneMessageId_YieldOneAddedAndTheRestDuplicates()
    {
        var (repository, dataSource) = await CreateAsync();
        var submissions = Enumerable.Range(0, 8).Select(_ => Submission("<race@mail.test>")).ToList();

        var results = await Task.WhenAll(submissions.Select(
            submission => repository.AddReceivedAsync(submission, [Attachment(submission.Id)], CancellationToken.None)));

        Assert.Equal(1, results.Count(result => result.Status == AddSubmissionStatus.Added));
        Assert.Equal(7, results.Count(result => result.Status == AddSubmissionStatus.Duplicate));
        var winner = results.Single(result => result.Status == AddSubmissionStatus.Added).SubmissionId;
        Assert.All(results, result => Assert.Equal(winner, result.SubmissionId));
        Assert.Equal(1, await CountAsync(dataSource, "email_submissions"));
        Assert.Equal(1, await CountAsync(dataSource, "email_attachments"));
    }

    [Fact]
    public async Task AddReceived_ConcurrentResendsOfAFailedMessage_YieldOneRequeuedAndTheRestDuplicates()
    {
        var (repository, dataSource) = await CreateAsync();
        var first = Submission("<race2@mail.test>");
        await repository.AddReceivedAsync(first, [Attachment(first.Id)], CancellationToken.None);
        await SetStatusAsync(dataSource, first.Id, "failed");
        var resends = Enumerable.Range(0, 6).Select(_ => Submission("<race2@mail.test>")).ToList();

        var results = await Task.WhenAll(resends.Select(
            resend => repository.AddReceivedAsync(resend, [Attachment(resend.Id)], CancellationToken.None)));

        Assert.Equal(1, results.Count(result => result.Status == AddSubmissionStatus.Requeued));
        Assert.Equal(5, results.Count(result => result.Status == AddSubmissionStatus.Duplicate));
        Assert.Equal(1, await CountAsync(dataSource, "email_submissions"));
        Assert.Equal(1, await CountAsync(dataSource, "email_attachments"));
    }
}
