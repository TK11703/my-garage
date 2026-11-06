using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Notifications;
using MyGarage.Database.Reminders;
using MyGarage.Database.Tests.Common;
using Npgsql;

namespace MyGarage.Database.Tests.Reminders;

[Collection(DatabaseCollection.Name)]
public class ReminderNotificationTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private async Task<(NpgsqlDataSource DataSource, ReminderRepository Reminders, NotificationRepository Notifications)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        return (dataSource, new ReminderRepository(dataSource, scripts), new NotificationRepository(dataSource, scripts));
    }

    private static Task<bool> NotifyAsync(
        ReminderRepository reminders, string ownerId, Guid reminderId, string? expected, string state, string title = "Oil change on 2022 Ford F-150 is overdue") =>
        reminders.InsertNotificationAndAdvanceAsync(
            ownerId, reminderId, expected, state, Guid.CreateVersion7(), state == "overdue" ? "reminder_overdue" : "reminder_due_soon", title, Now, CancellationToken.None);

    [Fact]
    public async Task ListForEvaluation_ReturnsActiveReminders_WithTheOwnerAndTheCurrentMileage()
    {
        var (dataSource, reminders, _) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1", mileage: 30_000, latestOdometer: 41_000);
        var active = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId, "Active", notifiedState: "due_soon");
        await ReminderTestData.InsertReminderAsync(dataSource, vehicleId, "Paused", isPaused: true);

        var list = await reminders.ListForEvaluationAsync(null, null, CancellationToken.None);

        var item = Assert.Single(list);
        Assert.Equal(active, item.Id);
        Assert.Equal("owner-1", item.OwnerId);
        Assert.Equal(41_000, item.CurrentMileage);
        Assert.Equal("due_soon", item.NotifiedState);
        Assert.Equal("F-150", item.VehicleModel);
    }

    [Fact]
    public async Task ListForEvaluation_CanBeNarrowedToOneVehicleOrOneOwner()
    {
        var (dataSource, reminders, _) = await CreateAsync();
        var one = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var two = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var three = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-2");
        var a = await ReminderTestData.InsertReminderAsync(dataSource, one);
        var b = await ReminderTestData.InsertReminderAsync(dataSource, two);
        var c = await ReminderTestData.InsertReminderAsync(dataSource, three);

        Assert.Equal([a], (await reminders.ListForEvaluationAsync("owner-1", one, CancellationToken.None)).Select(item => item.Id));
        Assert.Equal([a, b], (await reminders.ListForEvaluationAsync("owner-1", null, CancellationToken.None)).Select(item => item.Id).Order());
        Assert.Equal([c], (await reminders.ListForEvaluationAsync("owner-2", null, CancellationToken.None)).Select(item => item.Id));
        Assert.Empty(await reminders.ListForEvaluationAsync("owner-2", one, CancellationToken.None));
    }

    [Fact]
    public async Task TryAdvanceNotifiedState_OnlySucceedsFromTheExpectedState()
    {
        var (dataSource, reminders, _) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource);
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);

        Assert.False(await reminders.TryAdvanceNotifiedStateAsync(reminderId, "due_soon", "overdue", CancellationToken.None));
        Assert.True(await reminders.TryAdvanceNotifiedStateAsync(reminderId, null, "due_soon", CancellationToken.None));
        Assert.False(await reminders.TryAdvanceNotifiedStateAsync(reminderId, null, "due_soon", CancellationToken.None));
        Assert.True(await reminders.TryAdvanceNotifiedStateAsync(reminderId, "due_soon", null, CancellationToken.None));
    }

    [Fact]
    public async Task InsertNotificationAndAdvance_WritesTheNotificationAndTheState_OrNeither()
    {
        var (dataSource, reminders, notifications) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);

        Assert.True(await NotifyAsync(reminders, "owner-1", reminderId, null, "due_soon"));
        Assert.False(await NotifyAsync(reminders, "owner-1", reminderId, null, "due_soon"));

        Assert.Equal(1, await ReminderTestData.CountAsync(dataSource, "notifications"));
        var listed = Assert.Single(await notifications.ListAsync("owner-1", false, 10, CancellationToken.None));
        Assert.Equal("reminder_due_soon", listed.Kind);
        Assert.Equal(reminderId, listed.ReminderId);
        Assert.Equal(vehicleId, listed.VehicleId);
        Assert.Null(listed.DraftId);
        Assert.Equal("due_soon", await ReadStateAsync(dataSource, reminderId));
    }

    [Fact]
    public async Task InsertNotificationAndAdvance_TwoConcurrentCallsRaiseOneNotification()
    {
        var (dataSource, reminders, _) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => NotifyAsync(reminders, "owner-1", reminderId, null, "overdue")));

        Assert.Single(results, true);
        Assert.Equal(1, await ReminderTestData.CountAsync(dataSource, "notifications"));
    }

    [Fact]
    public async Task List_AndUnreadCount_IncludeReminderNotifications_AndDraftNotificationsKeepNullReminderFields()
    {
        var (dataSource, reminders, notifications) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);
        await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-1", createdAt: Now.AddHours(-1));
        await NotifyAsync(reminders, "owner-1", reminderId, null, "overdue");

        var list = await notifications.ListAsync("owner-1", false, 10, CancellationToken.None);

        Assert.Equal(["reminder_overdue", "draft_ready"], list.Select(item => item.Kind));
        Assert.Null(list[1].ReminderId);
        Assert.Null(list[1].VehicleId);
        Assert.Equal(2, await notifications.GetUnreadCountAsync("owner-1", CancellationToken.None));
    }

    [Fact]
    public async Task MarkReadForReminder_ReadsOnlyThatOwnersNotificationsForThatReminder()
    {
        var (dataSource, reminders, notifications) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var other = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId, "A");
        var otherReminder = await ReminderTestData.InsertReminderAsync(dataSource, other, "B");
        await NotifyAsync(reminders, "owner-1", reminderId, null, "due_soon");
        await NotifyAsync(reminders, "owner-1", otherReminder, null, "due_soon");

        await notifications.MarkReadForReminderAsync("owner-2", reminderId, Now, CancellationToken.None);
        Assert.Equal(2, await notifications.GetUnreadCountAsync("owner-1", CancellationToken.None));

        await notifications.MarkReadForReminderAsync("owner-1", reminderId, Now, CancellationToken.None);
        Assert.Equal(1, await notifications.GetUnreadCountAsync("owner-1", CancellationToken.None));
    }

    [Fact]
    public async Task DeletingAReminderOrItsVehicle_RemovesItsNotifications()
    {
        var (dataSource, reminders, _) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);
        await NotifyAsync(reminders, "owner-1", reminderId, null, "overdue");

        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("DELETE FROM vehicles WHERE id = @vehicleId", new { vehicleId });

        Assert.Equal(0, await ReminderTestData.CountAsync(dataSource, "notifications"));
    }

    private static async Task<string?> ReadStateAsync(NpgsqlDataSource dataSource, Guid reminderId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<string?>("SELECT notified_state FROM reminders WHERE id = @reminderId", new { reminderId });
    }
}
