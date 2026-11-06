using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Reminders;
using MyGarage.Database.Tests.Common;
using Npgsql;

namespace MyGarage.Database.Tests.Reminders;

[Collection(DatabaseCollection.Name)]
public class ReminderManageTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T1 = TestData.Created.AddDays(10);

    private async Task<(NpgsqlDataSource DataSource, ReminderRepository Reminders)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (dataSource, new ReminderRepository(dataSource, new SqlScriptStore()));
    }

    private static ReminderValues Changed() =>
        new("Hitch check", "equipment", "Trailer hitch", null, 24, new DateOnly(2026, 9, 1), null);

    [Fact]
    public async Task Update_ReplacesTheValues_KeepsCreationAndPausedState_AndStampsTheUpdate()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId, isPaused: true, notifiedState: "due_soon");

        var updated = await reminders.UpdateAsync("owner-1", vehicleId, reminderId, Changed(), T1, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("Hitch check", updated.Name);
        Assert.Equal("equipment", updated.AppliesTo);
        Assert.Equal("Trailer hitch", updated.Subject);
        Assert.Null(updated.IntervalMiles);
        Assert.Equal(24, updated.IntervalMonths);
        Assert.Equal(new DateOnly(2026, 9, 1), updated.LastDoneOn);
        Assert.Null(updated.LastDoneMileage);
        Assert.True(updated.IsPaused);
        Assert.Equal("due_soon", updated.NotifiedState);
        Assert.Equal(TestData.Created, updated.CreatedAt);
        Assert.Equal(T1, updated.UpdatedAt);
        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal("owner-1", await connection.ExecuteScalarAsync<string>("SELECT updated_by FROM reminders WHERE id = @reminderId", new { reminderId }));
        Assert.Equal("seed", await connection.ExecuteScalarAsync<string>("SELECT created_by FROM reminders WHERE id = @reminderId", new { reminderId }));
    }

    [Fact]
    public async Task Update_AnotherOwnersAnotherVehiclesOrAnUnknownReminder_ReturnsNull_AndChangesNothing()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var otherVehicle = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);

        Assert.Null(await reminders.UpdateAsync("owner-2", vehicleId, reminderId, Changed(), T1, CancellationToken.None));
        Assert.Null(await reminders.UpdateAsync("owner-1", otherVehicle, reminderId, Changed(), T1, CancellationToken.None));
        Assert.Null(await reminders.UpdateAsync("owner-1", vehicleId, Guid.NewGuid(), Changed(), T1, CancellationToken.None));

        Assert.Equal("Oil change", (await reminders.GetAsync("owner-1", vehicleId, reminderId, CancellationToken.None))!.Name);
    }

    [Fact]
    public async Task SetPaused_FlipsTheStateBothWays_ReturnsTheRecord_AndIsIdempotent()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);

        var paused = await reminders.SetPausedAsync("owner-1", vehicleId, reminderId, true, T1, CancellationToken.None);
        var again = await reminders.SetPausedAsync("owner-1", vehicleId, reminderId, true, T1, CancellationToken.None);
        var resumed = await reminders.SetPausedAsync("owner-1", vehicleId, reminderId, false, T1, CancellationToken.None);

        Assert.True(paused!.IsPaused);
        Assert.True(again!.IsPaused);
        Assert.False(resumed!.IsPaused);
        Assert.Equal(T1, resumed.UpdatedAt);
    }

    [Fact]
    public async Task SetPaused_AnotherOwnersReminder_ReturnsNull()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);

        Assert.Null(await reminders.SetPausedAsync("owner-2", vehicleId, reminderId, true, T1, CancellationToken.None));
        Assert.False((await reminders.GetAsync("owner-1", vehicleId, reminderId, CancellationToken.None))!.IsPaused);
    }

    [Fact]
    public async Task Delete_RemovesTheReminderItsCompletionsAndItsNotifications()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);
        await ReminderTestData.InsertCompletionAsync(dataSource, reminderId);
        await reminders.InsertNotificationAndAdvanceAsync("owner-1", reminderId, null, "overdue", Guid.CreateVersion7(), "reminder_overdue", "t", T1, CancellationToken.None);

        Assert.True(await reminders.DeleteAsync("owner-1", vehicleId, reminderId, CancellationToken.None));

        Assert.Equal(0, await ReminderTestData.CountAsync(dataSource, "reminders"));
        Assert.Equal(0, await ReminderTestData.CountAsync(dataSource, "reminder_completions"));
        Assert.Equal(0, await ReminderTestData.CountAsync(dataSource, "notifications"));
    }

    [Fact]
    public async Task Delete_AnotherOwnersOrAnUnknownReminder_ReturnsFalse_AndDeletesNothing()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);

        Assert.False(await reminders.DeleteAsync("owner-2", vehicleId, reminderId, CancellationToken.None));
        Assert.False(await reminders.DeleteAsync("owner-1", vehicleId, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(1, await ReminderTestData.CountAsync(dataSource, "reminders"));
    }

    [Fact]
    public async Task DeletingAVehicle_RemovesRemindersCompletionsAndNotifications()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);
        await ReminderTestData.InsertCompletionAsync(dataSource, reminderId);
        await reminders.InsertNotificationAndAdvanceAsync("owner-1", reminderId, null, "due_soon", Guid.CreateVersion7(), "reminder_due_soon", "t", T1, CancellationToken.None);

        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("DELETE FROM vehicles WHERE id = @vehicleId", new { vehicleId });

        Assert.Equal(0, await ReminderTestData.CountAsync(dataSource, "reminders"));
        Assert.Equal(0, await ReminderTestData.CountAsync(dataSource, "reminder_completions"));
        Assert.Equal(0, await ReminderTestData.CountAsync(dataSource, "notifications"));
    }
}
