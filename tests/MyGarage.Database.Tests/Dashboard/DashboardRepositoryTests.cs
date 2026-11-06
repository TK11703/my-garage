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
