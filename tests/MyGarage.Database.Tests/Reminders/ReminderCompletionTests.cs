using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Reminders;
using MyGarage.Database.Tests.Common;
using Npgsql;

namespace MyGarage.Database.Tests.Reminders;

[Collection(DatabaseCollection.Name)]
public class ReminderCompletionTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = TestData.Created;
    private static readonly DateTimeOffset T1 = TestData.Created.AddDays(30);

    private async Task<(NpgsqlDataSource DataSource, ReminderRepository Reminders)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (dataSource, new ReminderRepository(dataSource, new SqlScriptStore()));
    }

    private static Task<ReminderRecord?> CompleteAsync(
        ReminderRepository reminders, string ownerId, Guid vehicleId, Guid reminderId, DateOnly doneOn, int? mileage, bool raise = false) =>
        reminders.CompleteAsync(ownerId, vehicleId, reminderId, Guid.CreateVersion7(), doneOn, mileage, raise, T1, CancellationToken.None);

    private static async Task<int?> LatestOdometerAsync(NpgsqlDataSource dataSource, Guid vehicleId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int?>("SELECT latest_odometer_reading FROM vehicles WHERE id = @vehicleId", new { vehicleId });
    }

    [Fact]
    public async Task Complete_AddsACompletion_AndRollsTheLastDoneValuesForward()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1", mileage: 41_000);
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId, notifiedState: "overdue");

        var updated = await CompleteAsync(reminders, "owner-1", vehicleId, reminderId, new DateOnly(2026, 11, 3), 46_200);

        Assert.NotNull(updated);
        Assert.Equal(new DateOnly(2026, 11, 3), updated.LastDoneOn);
        Assert.Equal(46_200, updated.LastDoneMileage);
        Assert.Null(updated.NotifiedState);
        Assert.Equal(T1, updated.UpdatedAt);
        var completion = Assert.Single(await reminders.ListCompletionsAsync("owner-1", vehicleId, reminderId, CancellationToken.None));
        Assert.Equal(new DateOnly(2026, 11, 3), completion.DoneOn);
        Assert.Equal(46_200, completion.Mileage);
        Assert.Equal(T1, completion.CreatedAt);
    }

    [Fact]
    public async Task Complete_RecordsTheOwnerAsUpdaterAndCreator()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);

        await CompleteAsync(reminders, "owner-1", vehicleId, reminderId, new DateOnly(2026, 11, 3), 46_200);

        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal("owner-1", await connection.ExecuteScalarAsync<string>("SELECT updated_by FROM reminders WHERE id = @reminderId", new { reminderId }));
        Assert.Equal("owner-1", await connection.ExecuteScalarAsync<string>("SELECT created_by FROM reminder_completions WHERE reminder_id = @reminderId", new { reminderId }));
    }

    [Fact]
    public async Task Complete_WithoutAMileage_KeepsThePreviousLastDoneMileage()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId, intervalMiles: null, intervalMonths: 12, lastDoneMileage: 40_000);

        var updated = await CompleteAsync(reminders, "owner-1", vehicleId, reminderId, new DateOnly(2026, 11, 3), null);

        Assert.Equal(40_000, updated!.LastDoneMileage);
        Assert.Null((await reminders.ListCompletionsAsync("owner-1", vehicleId, reminderId, CancellationToken.None)).Single().Mileage);
    }

    [Fact]
    public async Task Complete_AnotherOwnersVehicleOrAnUnknownReminder_ReturnsNull_AndWritesNothing()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var otherVehicle = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);

        Assert.Null(await CompleteAsync(reminders, "owner-2", vehicleId, reminderId, new DateOnly(2026, 11, 3), 46_200, raise: true));
        Assert.Null(await CompleteAsync(reminders, "owner-1", otherVehicle, reminderId, new DateOnly(2026, 11, 3), 46_200));
        Assert.Null(await CompleteAsync(reminders, "owner-1", vehicleId, Guid.NewGuid(), new DateOnly(2026, 11, 3), 46_200));

        Assert.Equal(0, await ReminderTestData.CountAsync(dataSource, "reminder_completions"));
        Assert.Null(await LatestOdometerAsync(dataSource, vehicleId));
        Assert.Equal(new DateOnly(2026, 5, 1), (await reminders.GetAsync("owner-1", vehicleId, reminderId, CancellationToken.None))!.LastDoneOn);
    }

    [Fact]
    public async Task Complete_WhenAskedAndTheMileageIsAboveTheVehiclesCurrentMileage_RaisesTheLatestOdometerReading()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1", mileage: 30_000, latestOdometer: 41_000);
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);

        await CompleteAsync(reminders, "owner-1", vehicleId, reminderId, new DateOnly(2026, 11, 3), 46_200, raise: true);

        Assert.Equal(46_200, await LatestOdometerAsync(dataSource, vehicleId));
    }

    [Fact]
    public async Task Complete_WithoutBeingAsked_LeavesTheVehicleAlone()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1", latestOdometer: 41_000);
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);

        await CompleteAsync(reminders, "owner-1", vehicleId, reminderId, new DateOnly(2026, 11, 3), 46_200, raise: false);

        Assert.Equal(41_000, await LatestOdometerAsync(dataSource, vehicleId));
    }

    [Theory]
    [InlineData(41_000)]
    [InlineData(40_000)]
    public async Task Complete_AMileageNotAboveTheCurrentMileage_NeverLowersOrChangesTheVehicle(int mileage)
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1", latestOdometer: 40_500);
        await ServiceRecords.ServiceRecordTestData.InsertRecordAsync(dataSource, vehicleId, new DateOnly(2026, 6, 1), odometer: 41_000);
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId, lastDoneMileage: 30_000);

        await CompleteAsync(reminders, "owner-1", vehicleId, reminderId, new DateOnly(2026, 11, 3), mileage, raise: true);

        Assert.Equal(40_500, await LatestOdometerAsync(dataSource, vehicleId));
    }

    [Fact]
    public async Task Complete_AFailureInTheMiddleRollsEverythingBack()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId, lastDoneMileage: 30_000);

        // A mileage over the column limit fails the completion insert after the reminder update.
        await Assert.ThrowsAsync<PostgresException>(() =>
            CompleteAsync(reminders, "owner-1", vehicleId, reminderId, new DateOnly(2026, 11, 3), 10_000_000));

        var reminder = await reminders.GetAsync("owner-1", vehicleId, reminderId, CancellationToken.None);
        Assert.Equal(new DateOnly(2026, 5, 1), reminder!.LastDoneOn);
        Assert.Equal(30_000, reminder.LastDoneMileage);
        Assert.Equal(0, await ReminderTestData.CountAsync(dataSource, "reminder_completions"));
    }
}
