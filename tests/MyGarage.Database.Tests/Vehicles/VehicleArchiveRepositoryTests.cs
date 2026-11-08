using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Tests.Reminders;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Vehicles;

[Collection(DatabaseCollection.Name)]
public class VehicleArchiveRepositoryTests(PostgresFixture postgres)
{
    private const string Owner = "owner-a";
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 10, 30, 0, TimeSpan.Zero);

    private async Task<(NpgsqlDataSource DataSource, VehicleRepository Repository)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (dataSource, new VehicleRepository(dataSource, new SqlScriptStore()));
    }

    private static async Task<VehicleRecord> AddVehicleAsync(VehicleRepository repository, string owner, string model, string make = "Ford", string? color = null)
    {
        var vehicle = TestData.Vehicle(owner, model, make, color: color);
        await repository.InsertAsync(vehicle, CancellationToken.None);
        return vehicle;
    }

    private static Task InsertReminderNotificationAsync(NpgsqlDataSource dataSource, Guid reminderId, bool read = false) =>
        Execute(dataSource,
            "INSERT INTO notifications (id, owner_id, kind, reminder_id, title, created_at, read_at) " +
            "VALUES (gen_random_uuid(), 'owner-a', 'reminder_overdue', @reminderId, 'Title', now(), CASE WHEN @read THEN TIMESTAMPTZ '2026-11-01 00:00:00+00' END)",
            new { reminderId, read });

    private static async Task Execute(NpgsqlDataSource dataSource, string sql, object parameters)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    private static async Task<T> Scalar<T>(NpgsqlDataSource dataSource, string sql, object? parameters = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? default!;
    }

    [Fact]
    public async Task Archive_SetsArchivedAtAndAttribution_ThenReportsAlreadyInState()
    {
        var (dataSource, repository) = await CreateAsync();
        var vehicle = await AddVehicleAsync(repository, Owner, "F-150");

        Assert.Equal(VehicleStateChange.Changed, await repository.ArchiveAsync(Owner, vehicle.Id, Now, CancellationToken.None));
        var loaded = await repository.GetByIdAsync(Owner, vehicle.Id, CancellationToken.None);

        Assert.Equal(Now, loaded!.ArchivedAt);
        Assert.Equal(Now, loaded.UpdatedAt);
        Assert.Equal(Owner, loaded.UpdatedBy);
        Assert.Equal(VehicleStateChange.AlreadyInState, await repository.ArchiveAsync(Owner, vehicle.Id, Now.AddHours(1), CancellationToken.None));
        Assert.Equal(Now, (await repository.GetByIdAsync(Owner, vehicle.Id, CancellationToken.None))!.ArchivedAt);
        Assert.Equal(1, await Scalar<int>(dataSource, "SELECT count(*) FROM vehicles WHERE archived_at IS NOT NULL"));
    }

    [Fact]
    public async Task Archive_UnknownAndForeignVehicles_AreNotFoundAndChangeNothing()
    {
        var (_, repository) = await CreateAsync();
        var vehicle = await AddVehicleAsync(repository, Owner, "F-150");

        Assert.Equal(VehicleStateChange.NotFound, await repository.ArchiveAsync(Owner, Guid.NewGuid(), Now, CancellationToken.None));
        Assert.Equal(VehicleStateChange.NotFound, await repository.ArchiveAsync("owner-b", vehicle.Id, Now, CancellationToken.None));
        Assert.Null((await repository.GetByIdAsync(Owner, vehicle.Id, CancellationToken.None))!.ArchivedAt);
    }

    [Fact]
    public async Task Archive_KeepsEveryReminderRowExactlyAsItWas()
    {
        var (dataSource, repository) = await CreateAsync();
        var vehicle = await AddVehicleAsync(repository, Owner, "F-150");
        var paused = await ReminderTestData.InsertReminderAsync(dataSource, vehicle.Id, "Paused one", isPaused: true);
        var notified = await ReminderTestData.InsertReminderAsync(dataSource, vehicle.Id, "Notified one", notifiedState: "overdue");
        await ReminderTestData.InsertCompletionAsync(dataSource, notified);

        await repository.ArchiveAsync(Owner, vehicle.Id, Now, CancellationToken.None);

        Assert.True(await Scalar<bool>(dataSource, "SELECT is_paused FROM reminders WHERE id = @paused", new { paused }));
        Assert.Equal("overdue", await Scalar<string>(dataSource, "SELECT notified_state FROM reminders WHERE id = @notified", new { notified }));
        Assert.Equal(2, await Scalar<int>(dataSource, "SELECT count(*) FROM reminders"));
        Assert.Equal(1, await Scalar<int>(dataSource, "SELECT count(*) FROM reminder_completions"));
    }

    [Fact]
    public async Task Archive_MarksTheVehiclesUnreadReminderNoticesRead_AndNothingElse()
    {
        var (dataSource, repository) = await CreateAsync();
        var archived = await AddVehicleAsync(repository, Owner, "F-150");
        var other = await AddVehicleAsync(repository, Owner, "Outback");
        var archivedReminder = await ReminderTestData.InsertReminderAsync(dataSource, archived.Id, "A");
        var otherReminder = await ReminderTestData.InsertReminderAsync(dataSource, other.Id, "B");
        await InsertReminderNotificationAsync(dataSource, archivedReminder);
        await InsertReminderNotificationAsync(dataSource, archivedReminder, read: true);
        await InsertReminderNotificationAsync(dataSource, otherReminder);

        await repository.ArchiveAsync(Owner, archived.Id, Now, CancellationToken.None);

        Assert.Equal(0, await Scalar<int>(dataSource, "SELECT count(*) FROM notifications WHERE reminder_id = @archivedReminder AND read_at IS NULL", new { archivedReminder }));
        Assert.Equal(1, await Scalar<int>(dataSource, "SELECT count(*) FROM notifications WHERE reminder_id = @archivedReminder AND read_at = @Now", new { archivedReminder, Now }));
        Assert.Equal(1, await Scalar<int>(dataSource, "SELECT count(*) FROM notifications WHERE reminder_id = @otherReminder AND read_at IS NULL", new { otherReminder }));
    }

    [Fact]
    public async Task List_DefaultsToActive_ArchivedShowsOnlyArchived_AllShowsBoth()
    {
        var (_, repository) = await CreateAsync();
        var active = await AddVehicleAsync(repository, Owner, "F-150");
        var archived = await AddVehicleAsync(repository, Owner, "Outback", "Subaru");
        await repository.ArchiveAsync(Owner, archived.Id, Now, CancellationToken.None);

        var defaults = await repository.ListPageAsync(Owner, new VehicleSearchCriteria(), 1, 50, CancellationToken.None);
        var onlyArchived = await repository.ListPageAsync(Owner, new VehicleSearchCriteria { Status = "archived" }, 1, 50, CancellationToken.None);
        var all = await repository.ListPageAsync(Owner, new VehicleSearchCriteria { Status = "all" }, 1, 50, CancellationToken.None);

        Assert.Equal([active.Id], defaults.Items.Select(item => item.Id));
        Assert.Equal(1, defaults.TotalCount);
        Assert.Null(defaults.Items[0].ArchivedAt);
        Assert.Equal([archived.Id], onlyArchived.Items.Select(item => item.Id));
        Assert.Equal(Now, onlyArchived.Items[0].ArchivedAt);
        Assert.Equal(2, all.TotalCount);
        Assert.Equal(2, all.Items.Count);
    }

    [Fact]
    public async Task List_StatusCombinesWithSearchAndFilters()
    {
        var (_, repository) = await CreateAsync();
        await AddVehicleAsync(repository, Owner, "F-150", "Ford", "Red");
        var oldFord = await AddVehicleAsync(repository, Owner, "Mustang", "Ford", "Blue");
        var subaru = await AddVehicleAsync(repository, Owner, "Outback", "Subaru", "Blue");
        await repository.ArchiveAsync(Owner, oldFord.Id, Now, CancellationToken.None);
        await repository.ArchiveAsync(Owner, subaru.Id, Now, CancellationToken.None);

        var result = await repository.ListPageAsync(Owner, new VehicleSearchCriteria { Status = "archived", Make = "ford", Query = "must" }, 1, 50, CancellationToken.None);
        var none = await repository.ListPageAsync(Owner, new VehicleSearchCriteria { Status = "active", Make = "subaru" }, 1, 50, CancellationToken.None);

        Assert.Equal([oldFord.Id], result.Items.Select(item => item.Id));
        Assert.Equal(1, result.TotalCount);
        Assert.Empty(none.Items);
    }

    [Fact]
    public async Task FilterOptions_FollowTheChosenStatus()
    {
        var (_, repository) = await CreateAsync();
        await AddVehicleAsync(repository, Owner, "F-150", "Ford", "Red");
        var archived = await AddVehicleAsync(repository, Owner, "Outback", "Subaru", "Blue");
        await repository.ArchiveAsync(Owner, archived.Id, Now, CancellationToken.None);

        var active = await repository.GetFilterOptionsAsync(Owner, "active", CancellationToken.None);
        var onlyArchived = await repository.GetFilterOptionsAsync(Owner, "archived", CancellationToken.None);
        var all = await repository.GetFilterOptionsAsync(Owner, "all", CancellationToken.None);
        var defaulted = await repository.GetFilterOptionsAsync(Owner, CancellationToken.None);

        Assert.Equal(["Ford"], active.Makes);
        Assert.Equal(["Subaru"], onlyArchived.Makes);
        Assert.Equal(["Blue"], onlyArchived.Colors);
        Assert.Equal(["Ford", "Subaru"], all.Makes);
        Assert.Equal(active.Makes, defaulted.Makes);
    }

    [Fact]
    public async Task Restore_ClearsArchivedAt_ReportsAlreadyInState_AndNotFound()
    {
        var (dataSource, repository) = await CreateAsync();
        var vehicle = await AddVehicleAsync(repository, Owner, "F-150");
        var reminder = await ReminderTestData.InsertReminderAsync(dataSource, vehicle.Id, isPaused: true, notifiedState: "due_soon");

        Assert.Equal(VehicleStateChange.AlreadyInState, await repository.RestoreAsync(Owner, vehicle.Id, Now, CancellationToken.None));
        Assert.Equal(VehicleStateChange.NotFound, await repository.RestoreAsync(Owner, Guid.NewGuid(), Now, CancellationToken.None));
        Assert.Equal(VehicleStateChange.NotFound, await repository.RestoreAsync("owner-b", vehicle.Id, Now, CancellationToken.None));
        await repository.ArchiveAsync(Owner, vehicle.Id, Now, CancellationToken.None);
        Assert.Equal(VehicleStateChange.NotFound, await repository.RestoreAsync("owner-b", vehicle.Id, Now, CancellationToken.None));

        var later = Now.AddDays(3);
        Assert.Equal(VehicleStateChange.Changed, await repository.RestoreAsync(Owner, vehicle.Id, later, CancellationToken.None));
        var loaded = await repository.GetByIdAsync(Owner, vehicle.Id, CancellationToken.None);

        Assert.Null(loaded!.ArchivedAt);
        Assert.Equal(later, loaded.UpdatedAt);
        Assert.Equal(Owner, loaded.UpdatedBy);
        Assert.True(await Scalar<bool>(dataSource, "SELECT is_paused FROM reminders WHERE id = @reminder", new { reminder }));
        Assert.Equal("due_soon", await Scalar<string>(dataSource, "SELECT notified_state FROM reminders WHERE id = @reminder", new { reminder }));
        Assert.Single((await repository.ListPageAsync(Owner, new VehicleSearchCriteria(), 1, 50, CancellationToken.None)).Items);
    }

    [Fact]
    public async Task Delete_RemovesAnArchivedVehicleAndEverythingAttached()
    {
        var (dataSource, repository) = await CreateAsync();
        var vehicle = await AddVehicleAsync(repository, Owner, "F-150");
        var reminder = await ReminderTestData.InsertReminderAsync(dataSource, vehicle.Id);
        await ReminderTestData.InsertCompletionAsync(dataSource, reminder);
        await InsertReminderNotificationAsync(dataSource, reminder);
        await repository.ArchiveAsync(Owner, vehicle.Id, Now, CancellationToken.None);

        Assert.True(await repository.DeleteAsync(Owner, vehicle.Id, CancellationToken.None));

        Assert.Equal(0, await Scalar<int>(dataSource, "SELECT count(*) FROM vehicles"));
        Assert.Equal(0, await Scalar<int>(dataSource, "SELECT count(*) FROM reminders"));
        Assert.Equal(0, await Scalar<int>(dataSource, "SELECT count(*) FROM reminder_completions"));
        Assert.Equal(0, await Scalar<int>(dataSource, "SELECT count(*) FROM notifications"));
    }
}
