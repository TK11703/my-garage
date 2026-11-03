using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.EmailIntake;
using MyGarage.Database.Tests.Common;
using Npgsql;

namespace MyGarage.Database.Tests.EmailIntake;

[Collection(DatabaseCollection.Name)]
public class EmailSubmissionQueueTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(10);

    private async Task<(EmailSubmissionRepository Repository, NpgsqlDataSource DataSource)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (new EmailSubmissionRepository(dataSource, new SqlScriptStore()), dataSource);
    }

    private sealed class QueueRow
    {
        public string Status { get; init; } = string.Empty;

        public DateTimeOffset? LockedUntil { get; init; }

        public int AttemptCount { get; init; }

        public DateTimeOffset NextAttemptAt { get; init; }

        public string? FailureReason { get; init; }

        public string? Sender { get; init; }

        public string? BodyText { get; init; }

        public DateTimeOffset? ContentPurgedAt { get; init; }
    }

    private static async Task<QueueRow> ReadAsync(NpgsqlDataSource dataSource, Guid id)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<QueueRow>(
            "SELECT status, locked_until, attempt_count, next_attempt_at, failure_reason, sender, body_text, content_purged_at FROM email_submissions WHERE id = @id",
            new { id });
    }

    [Fact]
    public async Task ClaimNext_ReturnsTheOldestDueQueuedSubmission_AndLeasesIt()
    {
        var (repository, dataSource) = await CreateAsync();
        var newer = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, nextAttemptAt: Now.AddMinutes(-1));
        var older = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, nextAttemptAt: Now.AddMinutes(-5));

        var claimed = await repository.ClaimNextAsync(Now, Lease, CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(older, claimed.Id);
        Assert.Equal("owner-a", claimed.OwnerId);
        Assert.Equal("Oil change today", claimed.BodyText);
        Assert.Equal(1, claimed.AttemptCount);
        var row = await ReadAsync(dataSource, older);
        Assert.Equal("processing", row.Status);
        Assert.Equal(Now + Lease, row.LockedUntil);
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal("queued", (await ReadAsync(dataSource, newer)).Status);
    }

    [Fact]
    public async Task ClaimNext_ReturnsNull_WhenNothingIsDue()
    {
        var (repository, dataSource) = await CreateAsync();
        await EmailIntakeTestData.InsertSubmissionAsync(dataSource, nextAttemptAt: Now.AddMinutes(1));
        await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "ready", nextAttemptAt: Now.AddMinutes(-10));
        await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "failed", nextAttemptAt: Now.AddMinutes(-10));
        await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "not_accepted", ownerId: null, nextAttemptAt: Now.AddMinutes(-10));

        Assert.Null(await repository.ClaimNextAsync(Now, Lease, CancellationToken.None));
    }

    [Fact]
    public async Task ClaimNext_ReclaimsAProcessingSubmissionWhoseLeaseExpired_ButNotOneWithALiveLease()
    {
        var (repository, dataSource) = await CreateAsync();
        var live = await EmailIntakeTestData.InsertSubmissionAsync(
            dataSource, status: "processing", nextAttemptAt: Now.AddMinutes(-20), lockedUntil: Now.AddMinutes(5), attemptCount: 1);
        var expired = await EmailIntakeTestData.InsertSubmissionAsync(
            dataSource, status: "processing", nextAttemptAt: Now.AddMinutes(-15), lockedUntil: Now.AddMinutes(-1), attemptCount: 1);

        var claimed = await repository.ClaimNextAsync(Now, Lease, CancellationToken.None);

        Assert.NotNull(claimed);
        Assert.Equal(expired, claimed.Id);
        Assert.Equal(2, claimed.AttemptCount);
        Assert.Null(await repository.ClaimNextAsync(Now, Lease, CancellationToken.None));
        Assert.Equal(Now.AddMinutes(5), (await ReadAsync(dataSource, live)).LockedUntil);
    }

    [Fact]
    public async Task ClaimNext_NeverHandsTheSameRowToTwoCallers()
    {
        var (repository, dataSource) = await CreateAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            ids.Add(await EmailIntakeTestData.InsertSubmissionAsync(dataSource, nextAttemptAt: Now.AddMinutes(-i - 1)));
        }

        var claims = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => repository.ClaimNextAsync(Now, Lease, CancellationToken.None)));

        var claimedIds = claims.Where(claim => claim is not null).Select(claim => claim!.Id).ToList();
        Assert.Equal(12, claimedIds.Count);
        Assert.Equal(12, claimedIds.Distinct().Count());
        Assert.Equal(ids.Order(), claimedIds.Order());
    }

    [Theory]
    [InlineData("ready")]
    [InlineData("not_applicable")]
    public async Task Complete_SetsTheFinalStatusAndReleasesTheLease(string status)
    {
        var (repository, dataSource) = await CreateAsync();
        var id = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "processing", lockedUntil: Now.AddMinutes(5), attemptCount: 1);

        await repository.CompleteAsync(id, status, Now, CancellationToken.None);

        var row = await ReadAsync(dataSource, id);
        Assert.Equal(status, row.Status);
        Assert.Null(row.LockedUntil);
        Assert.Null(row.FailureReason);
    }

    [Fact]
    public async Task Complete_NotApplicable_PurgesTheEmailContent_ButReadyKeepsItForReview()
    {
        var (repository, dataSource) = await CreateAsync();
        var ready = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "processing");
        var notApplicable = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "processing");

        await repository.CompleteAsync(ready, "ready", Now, CancellationToken.None);
        await repository.CompleteAsync(notApplicable, "not_applicable", Now, CancellationToken.None);

        var kept = await ReadAsync(dataSource, ready);
        Assert.Equal("owner@example.com", kept.Sender);
        Assert.Null(kept.ContentPurgedAt);
        var purged = await ReadAsync(dataSource, notApplicable);
        Assert.Null(purged.Sender);
        Assert.Null(purged.BodyText);
        Assert.Equal(Now, purged.ContentPurgedAt);
    }

    [Fact]
    public async Task Retry_ReturnsTheSubmissionToTheQueueWithTheNextAttemptAndOnlyAReasonCode()
    {
        var (repository, dataSource) = await CreateAsync();
        var id = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "processing", lockedUntil: Now.AddMinutes(5), attemptCount: 1);

        await repository.RetryAsync(id, Now.AddMinutes(1), "service_error", CancellationToken.None);

        var row = await ReadAsync(dataSource, id);
        Assert.Equal("queued", row.Status);
        Assert.Null(row.LockedUntil);
        Assert.Equal(Now.AddMinutes(1), row.NextAttemptAt);
        Assert.Equal("service_error", row.FailureReason);
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal("owner@example.com", row.Sender);
    }

    [Fact]
    public async Task Fail_SetsFailedWithOnlyTheReasonCode()
    {
        var (repository, dataSource) = await CreateAsync();
        var id = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "processing", lockedUntil: Now.AddMinutes(5), attemptCount: 3);

        await repository.FailAsync(id, "invalid_output", Now, CancellationToken.None);

        var row = await ReadAsync(dataSource, id);
        Assert.Equal("failed", row.Status);
        Assert.Null(row.LockedUntil);
        Assert.Equal("invalid_output", row.FailureReason);
        Assert.Null(row.BodyText);
    }

    [Fact]
    public async Task ListAttachments_ReturnsOnlyTheSubmissionsAttachmentsOfThatOwner()
    {
        var (repository, dataSource) = await CreateAsync();
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource);
        var other = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, ownerId: "owner-b");
        var mine = await EmailIntakeTestData.InsertAttachmentAsync(dataSource, submission, fileName: "a.pdf");
        await EmailIntakeTestData.InsertAttachmentAsync(dataSource, other, ownerId: "owner-b");

        var listed = await repository.ListAttachmentsAsync(submission, "owner-a", CancellationToken.None);
        var wrongOwner = await repository.ListAttachmentsAsync(submission, "owner-b", CancellationToken.None);

        Assert.Equal(mine, Assert.Single(listed).Id);
        Assert.Equal("a.pdf", listed[0].FileName);
        Assert.Empty(wrongOwner);
    }
}
