using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Dashboard;
using MyGarage.Database.Reminders;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Tests.ServiceRecords;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Reminders;

// Archiving a vehicle disables its reminders without touching them, and the garage figures follow the fuel-and-charging exception (specs/014-archive-vehicle).
[Collection(DatabaseCollection.Name)]
public class ArchivedVehicleFiguresTests(PostgresFixture postgres)
{
    private const string Owner = "owner-1";
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    private async Task<(NpgsqlDataSource DataSource, VehicleRepository Vehicles)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (dataSource, new VehicleRepository(dataSource, new SqlScriptStore()));
    }

    private static async Task AddNoteAsync(NpgsqlDataSource dataSource, Guid vehicleId, string body)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO journal_notes (id, vehicle_id, body, created_at, updated_at, created_by, updated_by) VALUES (gen_random_uuid(), @vehicleId, @body, now(), now(), 'o', 'o')",
            new { vehicleId, body });
    }

    [Fact]
    public async Task OwnerReminderListAndEvaluation_LeaveOutArchivedVehicles_ButTheVehicleListsKeepThem()
    {
        var (dataSource, vehicles) = await CreateAsync();
        var scripts = new SqlScriptStore();
        var reminders = new ReminderRepository(dataSource, scripts);
        var active = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        var archived = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        var activeReminder = await ReminderTestData.InsertReminderAsync(dataSource, active, "Active one");
        var paused = await ReminderTestData.InsertReminderAsync(dataSource, archived, "Paused one", isPaused: true);
        var notified = await ReminderTestData.InsertReminderAsync(dataSource, archived, "Notified one", notifiedState: "overdue");
        await vehicles.ArchiveAsync(Owner, archived, Now, CancellationToken.None);

        var forOwner = await reminders.ListForOwnerAsync(Owner, CancellationToken.None);
        var evaluationForOwner = await reminders.ListForEvaluationAsync(Owner, null, CancellationToken.None);
        var evaluationForVehicle = await reminders.ListForEvaluationAsync(Owner, archived, CancellationToken.None);
        var evaluationForAll = await reminders.ListForEvaluationAsync(null, null, CancellationToken.None);
        var forVehicle = (await reminders.ListForVehicleAsync(Owner, archived, CancellationToken.None))!;
        var single = await reminders.GetAsync(Owner, archived, notified, CancellationToken.None);

        Assert.Equal([activeReminder], forOwner.Select(item => item.Id));
        Assert.All(forOwner, item => Assert.False(item.VehicleArchived));
        Assert.Equal([activeReminder], evaluationForOwner.Select(item => item.Id));
        Assert.Empty(evaluationForVehicle);
        Assert.Equal([activeReminder], evaluationForAll.Select(item => item.Id));
        Assert.Equal(new[] { paused, notified }.Order(), forVehicle.Select(item => item.Id).Order());
        Assert.All(forVehicle, item => Assert.True(item.VehicleArchived));
        Assert.True(single!.VehicleArchived);
        Assert.True(forVehicle.Single(item => item.Id == paused).IsPaused);
        Assert.Equal("overdue", single.NotifiedState);
    }

    [Fact]
    public async Task Counts_DropArchivedVehiclesAndTheirServiceRecords_ButKeepFuelEntriesAndDrafts()
    {
        var (dataSource, vehicles) = await CreateAsync();
        var dashboard = new DashboardRepository(dataSource, new SqlScriptStore());
        var active = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        var archived = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        await ServiceRecordTestData.InsertRecordAsync(dataSource, active, new DateOnly(2026, 1, 1));
        await ServiceRecordTestData.InsertRecordAsync(dataSource, archived, new DateOnly(2026, 2, 1));
        await ServiceRecordTestData.InsertRecordAsync(dataSource, archived, new DateOnly(2026, 3, 1));
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, active);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, archived);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, archived, "energy");
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "ready", ownerId: Owner);
        await EmailIntakeTestData.InsertDraftAsync(dataSource, Owner, submission, state: "needs_review", vehicleId: archived);
        await vehicles.ArchiveAsync(Owner, archived, Now, CancellationToken.None);

        var counts = await dashboard.GetCountsAsync(Owner, CancellationToken.None);

        Assert.Equal(1, counts.Vehicles);
        Assert.Equal(1, counts.ServiceRecords);
        Assert.Equal(3, counts.FuelEnergyEntries);
        Assert.Equal(1, counts.DraftsAwaitingReview);
    }

    [Fact]
    public async Task AGarageWhoseOnlyVehicleIsArchived_HasNoVehiclesOrRecords_ButStillHasItsFuelHistory()
    {
        var (dataSource, vehicles) = await CreateAsync();
        var dashboard = new DashboardRepository(dataSource, new SqlScriptStore());
        var only = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        await ServiceRecordTestData.InsertRecordAsync(dataSource, only, new DateOnly(2026, 1, 1));
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, only);
        await vehicles.ArchiveAsync(Owner, only, Now, CancellationToken.None);

        var counts = await dashboard.GetCountsAsync(Owner, CancellationToken.None);

        Assert.Equal(0, counts.Vehicles);
        Assert.Equal(0, counts.ServiceRecords);
        Assert.Equal(1, counts.FuelEnergyEntries);
    }

    [Fact]
    public async Task RecentActivity_LeavesOutArchivedVehiclesNotes()
    {
        var (dataSource, vehicles) = await CreateAsync();
        var dashboard = new DashboardRepository(dataSource, new SqlScriptStore());
        var active = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        var archived = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        await AddNoteAsync(dataSource, active, "active note");
        await AddNoteAsync(dataSource, archived, "archived note");
        await vehicles.ArchiveAsync(Owner, archived, Now, CancellationToken.None);

        var activity = await dashboard.GetRecentActivityAsync(Owner, 10, CancellationToken.None);

        Assert.Equal(["active note"], activity.Select(entry => entry.Body));
    }

    [Fact]
    public async Task FuelEnergySpend_IncludesArchivedVehicles_FlaggedAsArchived()
    {
        var (dataSource, vehicles) = await CreateAsync();
        var dashboard = new DashboardRepository(dataSource, new SqlScriptStore());
        var active = await ReminderTestData.InsertVehicleAsync(dataSource, Owner);
        var archived = await ReminderTestData.InsertVehicleAsync(dataSource, Owner, make: "Subaru", model: "Outback");
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, active, "fuel", new DateOnly(2026, 3, 1), totalCost: 40m);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, archived, "fuel", new DateOnly(2026, 3, 5), totalCost: 25m);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, archived, "energy", new DateOnly(2025, 12, 5), totalCost: 12m);
        await vehicles.ArchiveAsync(Owner, archived, Now, CancellationToken.None);

        var rows = await dashboard.GetFuelEnergySpendAsync(Owner, new DateOnly(2025, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None);

        Assert.Equal(3, rows.Count);
        Assert.Equal([false], rows.Where(row => row.VehicleId == active).Select(row => row.VehicleArchived));
        Assert.All(rows.Where(row => row.VehicleId == archived), row => Assert.True(row.VehicleArchived));
        Assert.Equal(25m, rows.Single(row => row.VehicleId == archived && row.Kind == "fuel").Total);
    }
}
