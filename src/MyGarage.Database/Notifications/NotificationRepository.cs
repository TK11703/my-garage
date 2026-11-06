using Dapper;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.Notifications;

public sealed class NotificationRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    public async Task<int> GetUnreadCountAsync(string ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            scripts.Get("Notifications/CountUnread"), new { OwnerId = ownerId }, cancellationToken: cancellationToken));
    }

    // Newest first; unreadOnly leaves out notifications already read.
    public async Task<IReadOnlyList<NotificationRecord>> ListAsync(string ownerId, bool unreadOnly, int take, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<NotificationRecord>(new CommandDefinition(
            scripts.Get("Notifications/List"), new { OwnerId = ownerId, UnreadOnly = unreadOnly, Take = take }, cancellationToken: cancellationToken))).ToList();
    }

    // False when the notification is not the owner's.
    public async Task<bool> MarkReadAsync(string ownerId, Guid notificationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            scripts.Get("Notifications/MarkRead"), new { NotificationId = notificationId, OwnerId = ownerId, Now = now }, cancellationToken: cancellationToken)) is not null;
    }

    public async Task MarkAllReadAsync(string ownerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Notifications/MarkAllRead"), new { OwnerId = ownerId, Now = now }, cancellationToken: cancellationToken));
    }

    // False when the notification is not the owner's.
    public async Task<bool> DeleteAsync(string ownerId, Guid notificationId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            scripts.Get("Notifications/Delete"), new { NotificationId = notificationId, OwnerId = ownerId }, cancellationToken: cancellationToken)) is not null;
    }

    public async Task DeleteReadAsync(string ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Notifications/DeleteRead"), new { OwnerId = ownerId }, cancellationToken: cancellationToken));
    }

    // Marking a reminder done or pausing it reads its alerts.
    public async Task MarkReadForReminderAsync(string ownerId, Guid reminderId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Notifications/MarkReadForReminder"), new { ReminderId = reminderId, OwnerId = ownerId, Now = now }, cancellationToken: cancellationToken));
    }

    // Opening a draft reads its notification.
    public async Task MarkReadForDraftAsync(string ownerId, Guid draftId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Notifications/MarkReadForDraft"), new { DraftId = draftId, OwnerId = ownerId, Now = now }, cancellationToken: cancellationToken));
    }

    // Drafts in needs_review or manual_entry.
    public async Task<int> CountDraftsAwaitingReviewAsync(string ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            scripts.Get("Notifications/CountDraftsAwaitingReview"), new { OwnerId = ownerId }, cancellationToken: cancellationToken));
    }
}
