using MyGarage.Database.Common;
using MyGarage.Database.Reminders;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Reminders;

[Collection(DatabaseCollection.Name)]
public class ReminderRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = TestData.Created;

    private async Task<(NpgsqlDataSource DataSource, ReminderRepository Reminders)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (dataSource, new ReminderRepository(dataSource, new SqlScriptStore()));
    }

    private static ReminderValues Values(
        string name = "Oil change",
        string appliesTo = "vehicle",
        string? subject = null,
        int? miles = 5_000,
        int? months = null,
        DateOnly? lastDoneOn = null,
        int? lastDoneMileage = 40_000) =>
        new(name, appliesTo, subject, miles, months, lastDoneOn ?? new DateOnly(2026, 5, 1), lastDoneMileage);

    private static Task<ReminderRecord?> AddAsync(ReminderRepository reminders, string ownerId, Guid vehicleId, ReminderValues? values = null, DateTimeOffset? at = null) =>
        reminders.AddAsync(ownerId, vehicleId, Guid.CreateVersion7(), values ?? Values(), at ?? T0, CancellationToken.None);

    [Fact]
    public async Task Add_StoresTheValuesWithAudit_AndReturnsTheVehicleParts()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1", year: 2022, make: "Ford", model: "F-150");

        var reminder = await AddAsync(reminders, "owner-1", vehicleId, Values("Hitch check", "equipment", "Trailer hitch", 3_000, 12));

        Assert.NotNull(reminder);
        Assert.Equal(vehicleId, reminder.VehicleId);
        Assert.Equal(2022, reminder.VehicleYear);
        Assert.Equal("Ford", reminder.VehicleMake);
        Assert.Equal("F-150", reminder.VehicleModel);
        Assert.Equal("Hitch check", reminder.Name);
        Assert.Equal("equipment", reminder.AppliesTo);
        Assert.Equal("Trailer hitch", reminder.Subject);
        Assert.Equal(3_000, reminder.IntervalMiles);
        Assert.Equal(12, reminder.IntervalMonths);
        Assert.Equal(new DateOnly(2026, 5, 1), reminder.LastDoneOn);
        Assert.Equal(40_000, reminder.LastDoneMileage);
        Assert.False(reminder.IsPaused);
        Assert.Null(reminder.NotifiedState);
        Assert.Equal(T0, reminder.CreatedAt);
        Assert.Equal(T0, reminder.UpdatedAt);
    }

    [Fact]
    public async Task Add_AnotherOwnersOrUnknownVehicle_ReturnsNullAndInsertsNothing()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");

        Assert.Null(await AddAsync(reminders, "owner-2", vehicleId));
        Assert.Null(await AddAsync(reminders, "owner-1", Guid.NewGuid()));

        Assert.Equal(0, await ReminderTestData.CountAsync(dataSource, "reminders"));
    }

    [Fact]
    public async Task Add_RecordsTheOwnerAsCreatorAndUpdater()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");

        var reminder = await AddAsync(reminders, "owner-1", vehicleId);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT created_by || '/' || updated_by FROM reminders WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", reminder!.Id);
        Assert.Equal("owner-1/owner-1", await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ListForVehicle_ReturnsTheOwnersRemindersOldestFirst_AndNullForAForeignVehicle()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var otherVehicle = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var second = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId, "Second", createdAt: T0.AddMinutes(1));
        var first = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId, "First", createdAt: T0);
        await ReminderTestData.InsertReminderAsync(dataSource, otherVehicle, "Elsewhere");

        var list = await reminders.ListForVehicleAsync("owner-1", vehicleId, CancellationToken.None);

        Assert.Equal([first, second], list!.Select(item => item.Id));
        Assert.Null(await reminders.ListForVehicleAsync("owner-2", vehicleId, CancellationToken.None));
        Assert.Null(await reminders.ListForVehicleAsync("owner-1", Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ListForVehicle_AVehicleWithoutReminders_ReturnsAnEmptyList()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");

        var list = await reminders.ListForVehicleAsync("owner-1", vehicleId, CancellationToken.None);

        Assert.NotNull(list);
        Assert.Empty(list);
    }

    [Fact]
    public async Task CurrentMileage_IsTheGreatestOfTheLatestRecordOdometerTheLatestReadingAndThePurchaseMileage()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1", mileage: 30_000, latestOdometer: 41_000);
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);

        Assert.Equal(41_000, (await reminders.GetAsync("owner-1", vehicleId, reminderId, CancellationToken.None))!.CurrentMileage);

        // The latest-dated record wins over an older one with a larger odometer.
        await ServiceRecords.ServiceRecordTestData.InsertRecordAsync(dataSource, vehicleId, new DateOnly(2026, 1, 1), odometer: 90_000);
        await ServiceRecords.ServiceRecordTestData.InsertRecordAsync(dataSource, vehicleId, new DateOnly(2026, 6, 1), odometer: 43_500);

        Assert.Equal(43_500, (await reminders.GetAsync("owner-1", vehicleId, reminderId, CancellationToken.None))!.CurrentMileage);
    }

    [Fact]
    public async Task CurrentMileage_IsNullWhenTheVehicleHasNoMileageAtAll()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);

        Assert.Null((await reminders.GetAsync("owner-1", vehicleId, reminderId, CancellationToken.None))!.CurrentMileage);
    }

    [Fact]
    public async Task CurrentMileage_EqualsTheMileageTheVehicleListShows()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1", mileage: 30_000, latestOdometer: 41_000);
        await ServiceRecords.ServiceRecordTestData.InsertRecordAsync(dataSource, vehicleId, new DateOnly(2026, 6, 1), odometer: 43_500);
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);
        var vehicles = new VehicleRepository(dataSource, new SqlScriptStore());

        var listed = await vehicles.ListPageAsync("owner-1", new VehicleSearchCriteria(), 1, 10, CancellationToken.None);
        var reminder = await reminders.GetAsync("owner-1", vehicleId, reminderId, CancellationToken.None);

        Assert.Equal(listed.Items.Single().Mileage, reminder!.CurrentMileage);
    }

    [Fact]
    public async Task Get_ReturnsNullForAnotherOwnerAnotherVehicleOrAnUnknownId()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var otherVehicle = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);

        Assert.NotNull(await reminders.GetAsync("owner-1", vehicleId, reminderId, CancellationToken.None));
        Assert.Null(await reminders.GetAsync("owner-2", vehicleId, reminderId, CancellationToken.None));
        Assert.Null(await reminders.GetAsync("owner-1", otherVehicle, reminderId, CancellationToken.None));
        Assert.Null(await reminders.GetAsync("owner-1", vehicleId, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ListForOwner_ReturnsEveryRemindersOfEveryVehicleTheOwnerHas_WithTheVehicleParts_AndNoOthers()
    {
        var (dataSource, reminders) = await CreateAsync();
        var first = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1", mileage: 10_000, make: "Ford", model: "F-150");
        var second = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1", year: null, make: "Honda", model: "Civic");
        var foreign = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-2");
        var a = await ReminderTestData.InsertReminderAsync(dataSource, first, "A");
        var b = await ReminderTestData.InsertReminderAsync(dataSource, second, "B");
        await ReminderTestData.InsertReminderAsync(dataSource, foreign, "Theirs");

        var list = await reminders.ListForOwnerAsync("owner-1", CancellationToken.None);

        Assert.Equal([a, b], list.Select(item => item.Id).Order());
        var forFirst = list.Single(item => item.Id == a);
        Assert.Equal(first, forFirst.VehicleId);
        Assert.Equal("F-150", forFirst.VehicleModel);
        Assert.Equal(10_000, forFirst.CurrentMileage);
        Assert.Null(list.Single(item => item.Id == b).VehicleYear);
    }

    [Fact]
    public async Task ListForOwner_AnOwnerWithoutVehiclesOrReminders_ReturnsAnEmptyList()
    {
        var (dataSource, reminders) = await CreateAsync();
        await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");

        Assert.Empty(await reminders.ListForOwnerAsync("owner-1", CancellationToken.None));
        Assert.Empty(await reminders.ListForOwnerAsync("nobody", CancellationToken.None));
    }

    [Fact]
    public async Task ListCompletions_ReturnsMostRecentFirst_AndNothingForAnotherOwner()
    {
        var (dataSource, reminders) = await CreateAsync();
        var vehicleId = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);
        var older = await ReminderTestData.InsertCompletionAsync(dataSource, reminderId, new DateOnly(2026, 1, 1), 35_000);
        var newer = await ReminderTestData.InsertCompletionAsync(dataSource, reminderId, new DateOnly(2026, 6, 1), 40_000);

        var completions = await reminders.ListCompletionsAsync("owner-1", vehicleId, reminderId, CancellationToken.None);
        var foreign = await reminders.ListCompletionsAsync("owner-2", vehicleId, reminderId, CancellationToken.None);

        Assert.Equal([newer, older], completions.Select(item => item.Id));
        Assert.Equal(40_000, completions[0].Mileage);
        Assert.Empty(foreign);
    }
}
