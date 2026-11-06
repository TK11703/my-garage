using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Notifications;
using MyGarage.Database.Tests.Common;
using Npgsql;

namespace MyGarage.Database.Tests.Notifications;

[Collection(DatabaseCollection.Name)]
public class NotificationRepositoryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task GetUnreadCount_CountsOnlyTheOwnersUnreadNotifications()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var repository = new NotificationRepository(dataSource, new SqlScriptStore());
        await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a");
        await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a");
        await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a", readAt: DateTimeOffset.UtcNow);
        await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-b");

        Assert.Equal(2, await repository.GetUnreadCountAsync("owner-a", CancellationToken.None));
        Assert.Equal(1, await repository.GetUnreadCountAsync("owner-b", CancellationToken.None));
        Assert.Equal(0, await repository.GetUnreadCountAsync("owner-c", CancellationToken.None));
    }

    [Fact]
    public async Task CountDraftsAwaitingReview_CountsNeedsReviewAndManualEntryDraftsOfThatOwnerOnly()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var repository = new NotificationRepository(dataSource, new SqlScriptStore());
        var submissionA = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "ready");
        var submissionB = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "ready", ownerId: "owner-b");
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submissionA, state: "needs_review");
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submissionA, state: "manual_entry", kind: null);
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submissionA, state: "included", decidedAt: DateTimeOffset.UtcNow);
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submissionA, state: "discarded", decidedAt: DateTimeOffset.UtcNow);
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submissionA, state: "expired", decidedAt: DateTimeOffset.UtcNow);
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-b", submissionB, state: "needs_review");

        Assert.Equal(2, await repository.CountDraftsAwaitingReviewAsync("owner-a", CancellationToken.None));
        Assert.Equal(1, await repository.CountDraftsAwaitingReviewAsync("owner-b", CancellationToken.None));
        Assert.Equal(0, await repository.CountDraftsAwaitingReviewAsync("owner-c", CancellationToken.None));
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static async Task<DateTimeOffset?> ReadAtAsync(NpgsqlDataSource dataSource, Guid id)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<DateTimeOffset?>("SELECT read_at FROM notifications WHERE id = @id", new { id });
    }

    [Fact]
    public async Task List_ReturnsTheOwnersNotificationsNewestFirst_LimitedAndOptionallyUnreadOnly()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var repository = new NotificationRepository(dataSource, new SqlScriptStore());
        var oldest = await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a", createdAt: Now.AddHours(-3));
        var read = await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a", createdAt: Now.AddHours(-2), readAt: Now.AddHours(-1));
        var newest = await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a", createdAt: Now.AddHours(-1), title: "New draft fuel entry ready to review");
        await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-b", createdAt: Now);

        var all = await repository.ListAsync("owner-a", unreadOnly: false, take: 5, CancellationToken.None);
        var unread = await repository.ListAsync("owner-a", unreadOnly: true, take: 5, CancellationToken.None);
        var limited = await repository.ListAsync("owner-a", unreadOnly: false, take: 2, CancellationToken.None);

        Assert.Equal([newest, read, oldest], all.Select(item => item.Id));
        Assert.Equal([newest, oldest], unread.Select(item => item.Id));
        Assert.Equal([newest, read], limited.Select(item => item.Id));
        Assert.All(all, item => Assert.Equal("owner-a", item.OwnerId));
        Assert.Equal("New draft fuel entry ready to review", all[0].Title);
        Assert.Equal("draft_ready", all[0].Kind);
        Assert.NotNull(all[1].ReadAt);
        Assert.Null(all[0].ReadAt);
        Assert.Empty(await repository.ListAsync("owner-c", false, 5, CancellationToken.None));
    }

    [Fact]
    public async Task MarkRead_MarksOnlyTheOwnersNotification_AndKeepsAnEarlierReadTime()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var repository = new NotificationRepository(dataSource, new SqlScriptStore());
        var mine = await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a");
        var already = await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a", readAt: Now.AddDays(-1));
        var theirs = await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-b");

        Assert.True(await repository.MarkReadAsync("owner-a", mine, Now, CancellationToken.None));
        Assert.True(await repository.MarkReadAsync("owner-a", already, Now, CancellationToken.None));
        Assert.False(await repository.MarkReadAsync("owner-a", theirs, Now, CancellationToken.None));
        Assert.False(await repository.MarkReadAsync("owner-a", Guid.NewGuid(), Now, CancellationToken.None));

        Assert.Equal(Now, await ReadAtAsync(dataSource, mine));
        Assert.Equal(Now.AddDays(-1), await ReadAtAsync(dataSource, already));
        Assert.Null(await ReadAtAsync(dataSource, theirs));
    }

    [Fact]
    public async Task MarkAllRead_TouchesOnlyTheOwnersUnreadRows()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var repository = new NotificationRepository(dataSource, new SqlScriptStore());
        var first = await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a");
        var second = await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a");
        var theirs = await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-b");

        await repository.MarkAllReadAsync("owner-a", Now, CancellationToken.None);

        Assert.Equal(Now, await ReadAtAsync(dataSource, first));
        Assert.Equal(Now, await ReadAtAsync(dataSource, second));
        Assert.Null(await ReadAtAsync(dataSource, theirs));
        Assert.Equal(0, await repository.GetUnreadCountAsync("owner-a", CancellationToken.None));
        Assert.Equal(1, await repository.GetUnreadCountAsync("owner-b", CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesOnlyTheOwnersNotification()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var repository = new NotificationRepository(dataSource, new SqlScriptStore());
        var mine = await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a");
        var theirs = await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-b");

        Assert.True(await repository.DeleteAsync("owner-a", mine, CancellationToken.None));
        Assert.False(await repository.DeleteAsync("owner-a", mine, CancellationToken.None));
        Assert.False(await repository.DeleteAsync("owner-a", theirs, CancellationToken.None));

        Assert.Empty(await repository.ListAsync("owner-a", false, 5, CancellationToken.None));
        Assert.Single(await repository.ListAsync("owner-b", false, 5, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteRead_RemovesOnlyTheOwnersReadRows()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var repository = new NotificationRepository(dataSource, new SqlScriptStore());
        await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a", readAt: Now);
        var unread = await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a");
        var theirs = await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-b", readAt: Now);

        await repository.DeleteReadAsync("owner-a", CancellationToken.None);

        Assert.Equal([unread], (await repository.ListAsync("owner-a", false, 5, CancellationToken.None)).Select(item => item.Id));
        Assert.Equal([theirs], (await repository.ListAsync("owner-b", false, 5, CancellationToken.None)).Select(item => item.Id));
    }

    [Fact]
    public async Task MarkReadForDraft_ReadsTheDraftsNotificationOfThatOwnerOnly()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var repository = new NotificationRepository(dataSource, new SqlScriptStore());
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "ready");
        var draft = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submission);
        var otherDraft = await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-a", submission);
        var notification = await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a", draft);
        var otherNotification = await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a", otherDraft);

        await repository.MarkReadForDraftAsync("owner-b", draft, Now, CancellationToken.None);
        Assert.Null(await ReadAtAsync(dataSource, notification));

        await repository.MarkReadForDraftAsync("owner-a", draft, Now, CancellationToken.None);
        Assert.Equal(Now, await ReadAtAsync(dataSource, notification));
        Assert.Null(await ReadAtAsync(dataSource, otherNotification));
    }
}
