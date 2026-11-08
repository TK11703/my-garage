using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MyGarage.Api.Reminders;
using MyGarage.Api.Tests.Common;
using MyGarage.Api.Tests.FuelEnergyEntries;
using MyGarage.Api.Tests.Reminders;
using MyGarage.Database.Reminders;
using MyGarage.Shared.Dashboard;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.Vehicles;
using static MyGarage.Api.Tests.Reminders.ReminderApiTestData;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.Dashboard;

// What archiving does to the garage figures and reminders through the API (specs/014-archive-vehicle).
[Collection("Logging")]
public class ArchivedVehicleFiguresEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-archfig-oid";
    private const string Bob = "bob-archfig-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<DashboardDto> GetAsync(HttpClient client) => client.GetFromJsonAsync<DashboardDto>("/api/dashboard")!;

    private static async Task ArchiveAsync(HttpClient client, Guid vehicleId) =>
        (await client.PostAsync($"/api/vehicles/{vehicleId}/archive", null)).EnsureSuccessStatusCode();

    private static async Task AddNoteAsync(HttpClient client, Guid vehicleId, string body) =>
        (await client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/notes", new JournalNoteRequest { Body = body })).EnsureSuccessStatusCode();

    private async Task<(HttpClient Client, Guid Active, Guid Archived)> SeedTwoVehiclesAsync()
    {
        var alice = factory.CreateClientForUser(Alice);
        var active = await CreateVehicleAsync(alice);
        var archived = await CreateVehicleAsync(alice);
        await CreateRecordAsync(alice, active.Id, new DateOnly(2026, 6, 1), odometer: 44_800);
        await CreateRecordAsync(alice, archived.Id, new DateOnly(2026, 6, 1), odometer: 44_800);
        await CreateRecordAsync(alice, archived.Id, new DateOnly(2026, 7, 1), odometer: 45_000);
        var thisMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        await FuelEnergyEntryTestData.CreateAsync(alice, active.Id, FuelEnergyEntryTestData.Request(FuelEnergyKind.Fuel, thisMonth, totalCost: 40m));
        await FuelEnergyEntryTestData.CreateAsync(alice, archived.Id, FuelEnergyEntryTestData.Request(FuelEnergyKind.Fuel, thisMonth, totalCost: 25m));
        await FuelEnergyEntryTestData.CreateAsync(alice, archived.Id, FuelEnergyEntryTestData.Request(FuelEnergyKind.Energy, thisMonth.AddMonths(-3), totalCost: 12m));
        await AddNoteAsync(alice, active.Id, "active note");
        await AddNoteAsync(alice, archived.Id, "archived note");
        await CreateAsync(alice, active.Id, Request("Active due soon"));
        await CreateAsync(alice, archived.Id, Request("Registration due", miles: null, time: Time(1, ReminderTimeUnit.Years), lastDoneOn: Today.AddDays(-400), lastDoneMileage: null));
        await CreateAsync(alice, archived.Id, Request("Tax due"));
        return (alice, active.Id, archived.Id);
    }

    [Fact]
    public async Task Archiving_RemovesTheVehicleFromEveryFigureExceptFuelAndCharging()
    {
        var (alice, _, archived) = await SeedTwoVehiclesAsync();
        var before = await GetAsync(alice);
        Assert.Equal(2, before.Vehicles);
        Assert.Equal(3, before.ServiceRecords);
        Assert.Equal(3, before.FuelEnergyEntries);
        Assert.Equal(2, before.RemindersOverdue);

        await ArchiveAsync(alice, archived);
        var after = await GetAsync(alice);

        Assert.Equal(1, after.Vehicles);
        Assert.Equal(1, after.ServiceRecords);
        Assert.Equal(3, after.FuelEnergyEntries);
        Assert.Equal(0, after.RemindersOverdue);
        Assert.Equal(1, after.RemindersDueSoon);
        Assert.Equal(["Active due soon"], after.Attention.Select(item => item.Name));
        Assert.Equal(["active note"], after.RecentActivity.Select(entry => entry.Excerpt));
    }

    [Fact]
    public async Task TheSpendChart_KeepsEveryMonthOfAnArchivedVehicle_FlaggedAsArchived()
    {
        var (alice, active, archived) = await SeedTwoVehiclesAsync();
        var before = (await GetAsync(alice)).FuelEnergySpend;
        await ArchiveAsync(alice, archived);

        var after = (await GetAsync(alice)).FuelEnergySpend;

        Assert.Equal(before.Select(month => (month.Fuel, month.Charging)), after.Select(month => (month.Fuel, month.Charging)));
        var vehicles = after.SelectMany(month => month.Vehicles).ToList();
        Assert.All(vehicles.Where(vehicle => vehicle.VehicleId == archived), vehicle => Assert.True(vehicle.VehicleArchived));
        Assert.All(vehicles.Where(vehicle => vehicle.VehicleId == active), vehicle => Assert.False(vehicle.VehicleArchived));
        Assert.Contains(vehicles, vehicle => vehicle.VehicleId == archived && vehicle.Fuel == 25m);
        Assert.Contains(vehicles, vehicle => vehicle.VehicleId == archived && vehicle.Charging == 12m);
    }

    [Fact]
    public async Task AGarageWhoseOnlyVehicleIsArchived_ShowsZerosExceptFuelAndCharging()
    {
        var alice = factory.CreateClientForUser(Alice);
        var only = await CreateVehicleAsync(alice);
        await CreateRecordAsync(alice, only.Id, new DateOnly(2026, 6, 1), odometer: 44_800);
        await FuelEnergyEntryTestData.CreateAsync(alice, only.Id, FuelEnergyEntryTestData.Request(FuelEnergyKind.Fuel, new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1), totalCost: 40m));
        await CreateAsync(alice, only.Id, Request("Due soon"));
        await ArchiveAsync(alice, only.Id);

        var dashboard = await GetAsync(alice);

        Assert.Equal(0, dashboard.Vehicles);
        Assert.Equal(0, dashboard.ServiceRecords);
        Assert.Equal(1, dashboard.FuelEnergyEntries);
        Assert.Equal(0, dashboard.RemindersOverdue);
        Assert.Equal(0, dashboard.RemindersDueSoon);
        Assert.Empty(dashboard.Attention);
        Assert.Empty(dashboard.RecentActivity);
        Assert.Equal(40m, dashboard.FuelEnergySpend.Sum(month => month.Fuel));
    }

    [Fact]
    public async Task Restoring_ReturnsEveryFigureToItsEarlierValue()
    {
        var (alice, _, archived) = await SeedTwoVehiclesAsync();
        var before = await GetAsync(alice);
        await ArchiveAsync(alice, archived);

        (await alice.PostAsync($"/api/vehicles/{archived}/restore", null)).EnsureSuccessStatusCode();
        var after = await GetAsync(alice);

        Assert.Equal(before.Vehicles, after.Vehicles);
        Assert.Equal(before.ServiceRecords, after.ServiceRecords);
        Assert.Equal(before.FuelEnergyEntries, after.FuelEnergyEntries);
        Assert.Equal(before.RemindersOverdue, after.RemindersOverdue);
        Assert.Equal(before.RemindersDueSoon, after.RemindersDueSoon);
        Assert.Equal(before.RecentActivity.Count, after.RecentActivity.Count);
    }

    [Fact]
    public async Task ArchivingOneUsersVehicle_ChangesNothingOnAnotherUsersDashboard()
    {
        var (_, _, archived) = await SeedTwoVehiclesAsync();
        var bob = factory.CreateClientForUser(Bob);
        await CreateVehicleAsync(bob);
        var before = await GetAsync(bob);

        await ArchiveAsync(factory.CreateClientForUser(Alice), archived);

        Assert.Equal(before.Vehicles, (await GetAsync(bob)).Vehicles);
    }

    [Fact]
    public async Task TheGarageReminderList_OmitsArchivedVehicles_AndTheVehiclesOwnListShowsThemAsArchived()
    {
        var (alice, active, archived) = await SeedTwoVehiclesAsync();
        var paused = await CreateAsync(alice, archived, Request("Paused one", miles: null, time: Time(1, ReminderTimeUnit.Years), lastDoneOn: Today, lastDoneMileage: null));
        await alice.PostAsync(Url(archived, paused.Id, "pause"), null);
        await ArchiveAsync(alice, archived);

        var garage = (await alice.GetFromJsonAsync<List<ReminderDto>>("/api/reminders"))!;
        var own = await ListAsync(alice, archived);
        var detail = (await alice.GetFromJsonAsync<ReminderDetailDto>(Url(archived, own[0].Id)))!;

        Assert.Equal(["Active due soon"], garage.Select(item => item.Name));
        Assert.All(garage, item => Assert.Equal(active, item.VehicleId));
        Assert.Equal(3, own.Count);
        Assert.All(own, item => Assert.Equal(ReminderStatus.Archived, item.Status));
        Assert.All(own, item => Assert.True(item.VehicleArchived));
        Assert.True(own.Single(item => item.Id == paused.Id).IsPaused);
        Assert.Equal(ReminderStatus.Archived, detail.Status);
        foreach (var status in new[] { "overdue", "due_soon", "paused", "upcoming" })
        {
            Assert.DoesNotContain((await alice.GetFromJsonAsync<List<ReminderDto>>($"/api/reminders?status={status}"))!, item => item.VehicleId == archived);
        }
    }

    [Fact]
    public async Task TheGarageReminderList_RejectsArchivedAsAStatusFilter()
    {
        var alice = factory.CreateClientForUser(Alice);

        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync("/api/reminders?status=archived")).StatusCode);
    }

    [Fact]
    public async Task TheEvaluatorAndSweep_RaiseNothingForAnArchivedVehicle_AndLeaveItsNotifiedStateAlone()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice, latestOdometer: 50_000);
        var overdue = await factory.InsertReminderAsync(vehicle, "Overdue one");
        var told = await factory.InsertReminderAsync(vehicle, "Told one", notifiedState: "due_soon");
        await factory.ExecuteScalarAsync("UPDATE vehicles SET archived_at = now() WHERE id = @id", ("id", vehicle));
        var evaluator = new ReminderEvaluator(factory.Services.GetRequiredService<ReminderRepository>(), TimeProvider.System, NullLogger<ReminderEvaluator>.Instance);

        var byVehicle = await evaluator.EvaluateVehicleAsync(Alice, vehicle, CancellationToken.None);
        var byOwner = await evaluator.EvaluateOwnerAsync(Alice, CancellationToken.None);
        var everyone = await evaluator.EvaluateAllAsync(CancellationToken.None);

        Assert.Equal([0, 0, 0], [byVehicle, byOwner, everyone]);
        Assert.Empty(await factory.NotificationKindsAsync(Alice));
        Assert.Null(await factory.NotifiedStateAsync(overdue));
        Assert.Equal("due_soon", await factory.NotifiedStateAsync(told));
    }

    [Fact]
    public async Task Archiving_MarksTheVehiclesUnreadReminderNoticesRead()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        await factory.SetLatestOdometerAsync(vehicle.Id, 50_000);
        var reminder = await factory.InsertReminderAsync(vehicle.Id);
        var evaluator = new ReminderEvaluator(factory.Services.GetRequiredService<ReminderRepository>(), TimeProvider.System, NullLogger<ReminderEvaluator>.Instance);
        await evaluator.EvaluateVehicleAsync(Alice, vehicle.Id, CancellationToken.None);
        Assert.Equal(["reminder_overdue"], await factory.NotificationKindsAsync(Alice, reminder));
        Assert.Equal(1L, await factory.ExecuteScalarAsync("SELECT count(*) FROM notifications WHERE reminder_id = @reminder AND read_at IS NULL", ("reminder", reminder)));

        await ArchiveAsync(alice, vehicle.Id);

        Assert.Equal(0L, await factory.ExecuteScalarAsync("SELECT count(*) FROM notifications WHERE reminder_id = @reminder AND read_at IS NULL", ("reminder", reminder)));
        Assert.Equal(1L, await factory.ExecuteScalarAsync("SELECT count(*) FROM notifications WHERE reminder_id = @reminder", ("reminder", reminder)));
    }
}
