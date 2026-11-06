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
