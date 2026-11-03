using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyGarage.Api.Drafts;
using MyGarage.Api.Tests.Common;
using MyGarage.Database.Drafts;
using MyGarage.Database.EmailIntake;

namespace MyGarage.Api.Tests.Drafts;

public class DraftExpiryServiceTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-expiry-oid";
    private const string Bob = "bob-expiry-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // Wraps the intake store so a test can make file deletes fail.
    private sealed class FlakyIntakeStore(IIntakeBlobStore inner) : IIntakeBlobStore
    {
        public bool FailDeletes { get; set; }

        public Task EnsureContainerAsync(CancellationToken cancellationToken) => inner.EnsureContainerAsync(cancellationToken);

        public Task UploadAsync(Guid submissionId, Guid attachmentId, Stream content, string contentType, CancellationToken cancellationToken) =>
            inner.UploadAsync(submissionId, attachmentId, content, contentType, cancellationToken);

        public Task<Stream?> OpenReadAsync(Guid submissionId, Guid attachmentId, CancellationToken cancellationToken) =>
            inner.OpenReadAsync(submissionId, attachmentId, cancellationToken);

        public Task DeleteAsync(Guid submissionId, Guid attachmentId, CancellationToken cancellationToken) =>
            FailDeletes ? throw new IntakeStorageUnavailableException("injected") : inner.DeleteAsync(submissionId, attachmentId, cancellationToken);

        public Task DeleteSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) => inner.DeleteSubmissionAsync(submissionId, cancellationToken);

        public Task<IReadOnlyList<string>> ListBlobNamesOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
            inner.ListBlobNamesOlderThanAsync(cutoff, cancellationToken);
    }

    private (DraftExpiryService Service, FlakyIntakeStore Store, TestTimeProvider Clock) NewService()
    {
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var store = new FlakyIntakeStore(factory.Services.GetRequiredService<IIntakeBlobStore>());
        var service = new DraftExpiryService(
            factory.Services.GetRequiredService<MaintenanceDraftRepository>(),
            store,
            clock,
            Options.Create(new DraftExpiryOptions()),
            NullLogger<DraftExpiryService>.Instance);
        return (service, store, clock);
    }

    private async Task<string> StateAsync(Guid draftId) => (string)(await factory.ExecuteScalarAsync("SELECT state FROM maintenance_drafts WHERE id = @id", ("id", draftId)))!;

    private sealed record Seeded(Guid Draft, Guid Submission, Guid? Attachment);

    private async Task<Seeded> SeedAsync(string ownerId, string state, DateTimeOffset expiresAt, bool withAttachment, string proposal = """{"kind":"service_record"}""")
    {
        var submission = await factory.InsertSubmissionAsync(ownerId, status: "ready");
        Guid? attachment = withAttachment ? await factory.InsertAttachmentAsync(submission, ownerId, content: IntakeClient.PdfBytes) : null;
        var draft = await factory.InsertDraftAsync(
            ownerId, submission, state, state == "manual_entry" ? null : "service_record", attachmentId: attachment, proposalJson: proposal, expiresAt: expiresAt);
        await factory.InsertNotificationAsync(ownerId, draft);
        return new Seeded(draft, submission, attachment);
    }

    [Fact]
    public async Task RunOnce_ExpiresDueDrafts_ClearingTheirContentFilesAndNotifications_AndLeavesTheRestAlone()
    {
        var (service, _, clock) = NewService();
        var now = clock.GetUtcNow();
        var dueWithFile = await SeedAsync(Alice, "needs_review", now.AddMinutes(-5), withAttachment: true);
        var dueManual = await SeedAsync(Bob, "manual_entry", now.AddDays(-1), withAttachment: true);
        var notYet = await SeedAsync(Alice, "needs_review", now.AddDays(3), withAttachment: true);
        var included = await SeedAsync(Alice, "included", now.AddDays(-9), withAttachment: false);

        var expired = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, expired);
        Assert.Equal("expired", await StateAsync(dueWithFile.Draft));
        Assert.Equal("expired", await StateAsync(dueManual.Draft));
        Assert.Equal("needs_review", await StateAsync(notYet.Draft));
        Assert.Equal("included", await StateAsync(included.Draft));
        Assert.Equal("{}", await factory.ExecuteScalarAsync("SELECT proposal::text FROM maintenance_drafts WHERE id = @id", ("id", dueWithFile.Draft)));
        Assert.False(await factory.IntakeBlobExistsAsync(dueWithFile.Submission, dueWithFile.Attachment!.Value));
        Assert.False(await factory.IntakeBlobExistsAsync(dueManual.Submission, dueManual.Attachment!.Value));
        Assert.True(await factory.IntakeBlobExistsAsync(notYet.Submission, notYet.Attachment!.Value));
        Assert.Equal(2L, await factory.CountRowsAsync("notifications"));
        Assert.Null(await factory.ExecuteScalarAsync("SELECT subject FROM email_submissions WHERE id = @id", ("id", dueWithFile.Submission)));
        Assert.NotNull(await factory.ExecuteScalarAsync("SELECT subject FROM email_submissions WHERE id = @id", ("id", notYet.Submission)));
        Assert.Equal(0, await service.RunOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RunOnce_WhenAFileDeleteFails_TheDraftStaysExpired_AndTheNextRunRetriesTheDelete()
    {
        var (service, store, _) = NewService();
        var due = await SeedAsync(Alice, "needs_review", DateTimeOffset.UtcNow.AddMinutes(-5), withAttachment: true);
        store.FailDeletes = true;

        var first = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal("expired", await StateAsync(due.Draft));
        Assert.True(await factory.IntakeBlobExistsAsync(due.Submission, due.Attachment!.Value));

        store.FailDeletes = false;
        var second = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, second);
        Assert.False(await factory.IntakeBlobExistsAsync(due.Submission, due.Attachment.Value));
    }

    [Fact]
    public async Task RunOnce_WithNothingDue_DoesNothing()
    {
        var (service, _, clock) = NewService();
        var open = await SeedAsync(Alice, "needs_review", clock.GetUtcNow().AddDays(10), withAttachment: true);

        Assert.Equal(0, await service.RunOnceAsync(CancellationToken.None));

        Assert.Equal("needs_review", await StateAsync(open.Draft));
        Assert.True(await factory.IntakeBlobExistsAsync(open.Submission, open.Attachment!.Value));
    }
}
