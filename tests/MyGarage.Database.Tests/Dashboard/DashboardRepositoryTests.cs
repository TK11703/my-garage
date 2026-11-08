using MyGarage.Database.Common;
using MyGarage.Database.Dashboard;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Tests.Reminders;
using MyGarage.Database.Tests.ServiceRecords;
using Npgsql;

namespace MyGarage.Database.Tests.Dashboard;

[Collection(DatabaseCollection.Name)]
public class DashboardRepositoryTests(PostgresFixture postgres)
{
    private async Task<(NpgsqlDataSource DataSource, DashboardRepository Dashboard)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (dataSource, new DashboardRepository(dataSource, new SqlScriptStore()));
    }

    [Fact]
    public async Task AnOwnerWithNothing_HasAllZeros()
    {
        var (_, dashboard) = await CreateAsync();

        var counts = await dashboard.GetCountsAsync("nobody", CancellationToken.None);

        Assert.Equal(0, counts.Vehicles);
        Assert.Equal(0, counts.ServiceRecords);
        Assert.Equal(0, counts.FuelEnergyEntries);
        Assert.Equal(0, counts.DraftsAwaitingReview);
    }

    [Fact]
    public async Task CountsTheOwnersVehiclesRecordsFuelEntriesAndOpenDrafts_AndNeverAnotherOwners()
    {
        var (dataSource, dashboard) = await CreateAsync();
        var one = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var two = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var theirs = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-2");
        await ServiceRecordTestData.InsertRecordAsync(dataSource, one, new DateOnly(2026, 1, 1));
        await ServiceRecordTestData.InsertRecordAsync(dataSource, one, new DateOnly(2026, 2, 1));
        await ServiceRecordTestData.InsertRecordAsync(dataSource, two, new DateOnly(2026, 3, 1));
        await ServiceRecordTestData.InsertRecordAsync(dataSource, theirs, new DateOnly(2026, 3, 1));
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, one);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, two, "energy");
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, theirs);
        var mine = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "ready", ownerId: "owner-1");
        var theirSubmission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "ready", ownerId: "owner-2");
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-1", mine, state: "needs_review");
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-1", mine, state: "manual_entry", kind: null);
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-1", mine, state: "included", decidedAt: DateTimeOffset.UtcNow);
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-1", mine, state: "discarded", decidedAt: DateTimeOffset.UtcNow);
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-2", theirSubmission, state: "needs_review");

        var counts = await dashboard.GetCountsAsync("owner-1", CancellationToken.None);

        Assert.Equal(2, counts.Vehicles);
        Assert.Equal(3, counts.ServiceRecords);
        Assert.Equal(2, counts.FuelEnergyEntries);
        Assert.Equal(2, counts.DraftsAwaitingReview);
    }

    [Fact]
    public async Task FuelEnergySpend_SumsPerMonthKindAndVehicle_ForTheOwner_WithinTheRange()
    {
        var (dataSource, dashboard) = await CreateAsync();
        var one = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var two = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var theirs = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-2");
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, one, "fuel", new DateOnly(2026, 3, 1), totalCost: 40m);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, two, "fuel", new DateOnly(2026, 3, 31), totalCost: 25.50m);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, one, "energy", new DateOnly(2026, 3, 15), totalCost: 12m);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, one, "fuel", new DateOnly(2026, 4, 2), totalCost: 30m);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, one, "fuel", new DateOnly(2026, 2, 28), totalCost: 99m);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, one, "fuel", new DateOnly(2026, 5, 1), totalCost: 99m);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, theirs, "fuel", new DateOnly(2026, 3, 10), totalCost: 500m);

        var spend = await dashboard.GetFuelEnergySpendAsync("owner-1", new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 30), CancellationToken.None);

        Assert.Equal(
            new[]
            {
                (new DateOnly(2026, 3, 1), "energy", one, 12m), (new DateOnly(2026, 3, 1), "fuel", one, 40m),
                (new DateOnly(2026, 3, 1), "fuel", two, 25.50m), (new DateOnly(2026, 4, 1), "fuel", one, 30m),
            }.OrderBy(row => row.Item1).ThenBy(row => row.Item2, StringComparer.Ordinal).ThenBy(row => row.Item3),
            spend.Select(row => (row.Month, row.Kind, row.VehicleId, row.Total)));
        Assert.All(spend, row => Assert.Equal((2022, "Ford", "F-150"), (row.VehicleYear, row.VehicleMake, row.VehicleModel)));
    }

    [Fact]
    public async Task FuelEnergySpend_ForAnOwnerWithNothing_IsEmpty()
    {
        var (_, dashboard) = await CreateAsync();

        Assert.Empty(await dashboard.GetFuelEnergySpendAsync("nobody", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None));
    }

    [Fact]
    public async Task RecentActivity_IsTheNewestJournalEntriesAcrossTheOwnersVehicles_AndNobodyElses()
    {
        var (dataSource, dashboard) = await CreateAsync();
        var one = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1", make: "Ford", model: "F-150", year: 2020);
        var two = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1", make: "Honda", model: "Civic", year: 2018);
        var theirs = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-2");
        var now = DateTimeOffset.UtcNow;
        await InsertNoteAsync(dataSource, one, "oldest", now.AddDays(-5), "Maintenance");
        var newest = await InsertNoteAsync(dataSource, two, "newest", now.AddMinutes(-1), null);
        var middle = await InsertNoteAsync(dataSource, one, "middle", now.AddHours(-2), "Trip");
        await InsertNoteAsync(dataSource, theirs, "not mine", now, null);

        var recent = await dashboard.GetRecentActivityAsync("owner-1", 10, CancellationToken.None);

        Assert.Equal(["newest", "middle", "oldest"], recent.Select(note => note.Body));
        Assert.Equal([newest, middle], recent.Take(2).Select(note => note.NoteId));
        Assert.Equal((two, 2018, "Honda", "Civic", (string?)null), (recent[0].VehicleId, recent[0].VehicleYear, recent[0].VehicleMake, recent[0].VehicleModel, recent[0].Category));
        Assert.Equal("Trip", recent[1].Category);
    }

    [Fact]
    public async Task RecentActivity_IsLimited_AndCarriesTheVehiclesMainPicture()
    {
        var (dataSource, dashboard) = await CreateAsync();
        var vehicle = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var bare = await ReminderTestData.InsertVehicleAsync(dataSource, "owner-1");
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 12; index++)
        {
            await InsertNoteAsync(dataSource, vehicle, $"note {index}", now.AddMinutes(-index), null);
        }

        await InsertNoteAsync(dataSource, bare, "bare vehicle", now.AddDays(-30), null);
        var imageId = await InsertMainImageAsync(dataSource, vehicle);

        var recent = await dashboard.GetRecentActivityAsync("owner-1", 10, CancellationToken.None);

        Assert.Equal(10, recent.Count);
        Assert.All(recent, note => Assert.Equal(imageId, note.VehicleMainImageId));
        var all = await dashboard.GetRecentActivityAsync("owner-1", 50, CancellationToken.None);
        Assert.Null(all.Single(note => note.Body == "bare vehicle").VehicleMainImageId);
    }

    [Fact]
    public async Task RecentActivity_ForAnOwnerWithNothing_IsEmpty()
    {
        var (_, dashboard) = await CreateAsync();

        Assert.Empty(await dashboard.GetRecentActivityAsync("nobody", 10, CancellationToken.None));
    }

    private static async Task<Guid> InsertNoteAsync(NpgsqlDataSource dataSource, Guid vehicleId, string body, DateTimeOffset createdAt, string? category)
    {
        var id = Guid.CreateVersion7();
        await using var connection = await dataSource.OpenConnectionAsync();
        await Dapper.SqlMapper.ExecuteAsync(
            connection,
            "INSERT INTO journal_notes (id, vehicle_id, category, body, created_at, updated_at, created_by, updated_by) VALUES (@id, @vehicleId, @category, @body, @createdAt, @createdAt, 'seed', 'seed')",
            new { id, vehicleId, category, body, createdAt });
        return id;
    }

    private static async Task<Guid> InsertMainImageAsync(NpgsqlDataSource dataSource, Guid vehicleId)
    {
        var id = Guid.CreateVersion7();
        await using var connection = await dataSource.OpenConnectionAsync();
        await Dapper.SqlMapper.ExecuteAsync(
            connection,
            "INSERT INTO vehicle_images (id, vehicle_id, content_type, size_bytes, is_main, created_at, created_by) VALUES (@id, @vehicleId, 'image/png', 10, true, now(), 'seed')",
            new { id, vehicleId });
        return id;
    }

    [Fact]
    public async Task TheDraftCount_MatchesTheNotificationsDraftsAwaitingReviewRule()
    {
        var (dataSource, dashboard) = await CreateAsync();
        var scripts = new SqlScriptStore();
        var notifications = new MyGarage.Database.Notifications.NotificationRepository(dataSource, scripts);
        var submission = await EmailIntakeTestData.InsertSubmissionAsync(dataSource, status: "ready", ownerId: "owner-1");
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-1", submission, state: "needs_review");
        await EmailIntakeTestData.InsertDraftAsync(dataSource, "owner-1", submission, state: "expired", decidedAt: DateTimeOffset.UtcNow);

        var counts = await dashboard.GetCountsAsync("owner-1", CancellationToken.None);

        Assert.Equal(await notifications.CountDraftsAwaitingReviewAsync("owner-1", CancellationToken.None), counts.DraftsAwaitingReview);
    }
}
