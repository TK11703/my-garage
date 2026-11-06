using System.Net;
using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Api.Tests.FuelEnergyEntries;
using MyGarage.Shared.Dashboard;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Reminders;
using static MyGarage.Api.Tests.Reminders.ReminderApiTestData;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.Dashboard;

public class DashboardEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-dash-oid";
    private const string Bob = "bob-dash-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<DashboardDto> GetAsync(HttpClient client) => client.GetFromJsonAsync<DashboardDto>("/api/dashboard")!;

    [Fact]
    public async Task AnEmptyGarage_ReturnsZerosAndNothingNeedingAttention()
    {
        var alice = factory.CreateClientForUser(Alice);

        var dashboard = await GetAsync(alice);

        Assert.Equal(0, dashboard.Vehicles);
        Assert.Equal(0, dashboard.ServiceRecords);
        Assert.Equal(0, dashboard.FuelEnergyEntries);
        Assert.Equal(0, dashboard.DraftsAwaitingReview);
        Assert.Equal(0, dashboard.RemindersOverdue);
        Assert.Equal(0, dashboard.RemindersDueSoon);
        Assert.Empty(dashboard.Attention);
    }

    [Fact]
    public async Task ReturnsTheCallersCounts_AndNothingFromAnotherUser()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var one = await CreateVehicleAsync(alice);
        await CreateVehicleAsync(alice);
        await CreateRecordAsync(alice, one.Id, new DateOnly(2026, 6, 1), odometer: 41_000);
        await CreateRecordAsync(alice, one.Id, new DateOnly(2026, 7, 1), odometer: 42_000);
        var bobsVehicle = await CreateVehicleAsync(bob);
        await CreateRecordAsync(bob, bobsVehicle.Id);
        var submission = await factory.InsertSubmissionAsync(Alice, status: "ready");
        await factory.InsertDraftAsync(Alice, submission, "needs_review", "service_record", null, null, "none", "{}");

        var dashboard = await GetAsync(alice);

        Assert.Equal(2, dashboard.Vehicles);
        Assert.Equal(2, dashboard.ServiceRecords);
        Assert.Equal(1, dashboard.DraftsAwaitingReview);
        Assert.Equal(1, (await GetAsync(bob)).Vehicles);
    }

    [Fact]
    public async Task ReminderCounts_FollowTheComputedStatus_PausedAndMileageNeededCountInNeither()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        var bare = await CreateVehicleAsync(alice);
        await CreateRecordAsync(alice, vehicle.Id, new DateOnly(2026, 6, 1), odometer: 44_800);
        await CreateAsync(alice, vehicle.Id, Request("Due soon"));
        await CreateAsync(alice, vehicle.Id, Request("Overdue", miles: null, time: Time(1, ReminderTimeUnit.Years), lastDoneOn: Today.AddDays(-400), lastDoneMileage: null));
        await CreateAsync(alice, vehicle.Id, Request("Upcoming", miles: null, time: Time(1, ReminderTimeUnit.Years), lastDoneOn: Today.AddDays(-10), lastDoneMileage: null));
        await CreateAsync(alice, bare.Id, Request("Needs mileage", lastDoneMileage: 1_000));
        var paused = await CreateAsync(alice, vehicle.Id, Request("Paused", miles: null, time: Time(1, ReminderTimeUnit.Years), lastDoneOn: Today.AddDays(-400), lastDoneMileage: null));
        await alice.PostAsync(Url(vehicle.Id, paused.Id, "pause"), null);

        var dashboard = await GetAsync(alice);

        Assert.Equal(1, dashboard.RemindersOverdue);
        Assert.Equal(1, dashboard.RemindersDueSoon);
    }

    [Fact]
    public async Task TheAttentionList_HoldsAtMostFive_OverdueFirstThenDueSoon_NeverUpcomingOrPaused()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        await CreateRecordAsync(alice, vehicle.Id, new DateOnly(2026, 6, 1), odometer: 44_800);
        for (var index = 0; index < 4; index++)
        {
            await CreateAsync(alice, vehicle.Id, Request($"Soon {index}", lastDoneMileage: 40_000));
        }

        for (var index = 0; index < 3; index++)
        {
            await CreateAsync(alice, vehicle.Id, Request($"Late {index}", miles: null, time: Time(1, ReminderTimeUnit.Years), lastDoneOn: Today.AddDays(-400 - index), lastDoneMileage: null));
        }

        await CreateAsync(alice, vehicle.Id, Request("Fine", miles: null, time: Time(1, ReminderTimeUnit.Years), lastDoneOn: Today, lastDoneMileage: null));

        var dashboard = await GetAsync(alice);

        Assert.Equal(5, dashboard.Attention.Count);
        Assert.Equal(
            [ReminderStatus.Overdue, ReminderStatus.Overdue, ReminderStatus.Overdue, ReminderStatus.DueSoon, ReminderStatus.DueSoon],
            dashboard.Attention.Select(item => item.Status));
        Assert.All(dashboard.Attention, item => Assert.Equal("2020 Ford F-150", item.VehicleName));
        Assert.Equal(3, dashboard.RemindersOverdue);
        Assert.Equal(4, dashboard.RemindersDueSoon);
    }

    [Fact]
    public async Task NeverReturnsAnotherUsersReminders()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(bob);
        await CreateRecordAsync(bob, vehicle.Id, new DateOnly(2026, 6, 1), odometer: 50_000);
        await CreateAsync(bob, vehicle.Id);

        var dashboard = await GetAsync(alice);

        Assert.Empty(dashboard.Attention);
        Assert.Equal(0, dashboard.RemindersOverdue);
    }

    [Fact]
    public async Task FuelEnergySpend_IsTheLast12MonthsOldestFirst_SplitByKind_AndScopedToTheCaller()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var one = await CreateVehicleAsync(alice);
        var two = await CreateVehicleAsync(alice);
        var bobs = await CreateVehicleAsync(bob);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        await FuelEnergyEntryTestData.CreateAsync(alice, one.Id, FuelEnergyEntryTestData.Request(FuelEnergyKind.Fuel, thisMonth, totalCost: 40m));
        await FuelEnergyEntryTestData.CreateAsync(alice, two.Id, FuelEnergyEntryTestData.Request(FuelEnergyKind.Fuel, thisMonth, totalCost: 10.25m));
        await FuelEnergyEntryTestData.CreateAsync(alice, one.Id, FuelEnergyEntryTestData.Request(FuelEnergyKind.Energy, thisMonth, totalCost: 7m));
        await FuelEnergyEntryTestData.CreateAsync(alice, one.Id, FuelEnergyEntryTestData.Request(FuelEnergyKind.Fuel, thisMonth.AddMonths(-11), totalCost: 30m));
        await FuelEnergyEntryTestData.CreateAsync(alice, one.Id, FuelEnergyEntryTestData.Request(FuelEnergyKind.Fuel, thisMonth.AddMonths(-12), totalCost: 99m));
        await FuelEnergyEntryTestData.CreateAsync(bob, bobs.Id, FuelEnergyEntryTestData.Request(FuelEnergyKind.Fuel, thisMonth, totalCost: 500m));

        var spend = (await GetAsync(alice)).FuelEnergySpend;

        Assert.Equal(12, spend.Count);
        Assert.Equal(thisMonth.AddMonths(-11), spend[0].Month);
        Assert.Equal(thisMonth, spend[^1].Month);
        Assert.Equal((30m, 0m), (spend[0].Fuel, spend[0].Charging));
        Assert.Equal((50.25m, 7m), (spend[^1].Fuel, spend[^1].Charging));
        Assert.All(spend.Skip(1).SkipLast(1), month => Assert.Equal((0m, 0m), (month.Fuel, month.Charging)));
    }

    [Fact]
    public async Task FuelEnergySpend_ForAnEmptyGarage_IsTwelveMonthsOfZeros()
    {
        var spend = (await GetAsync(factory.CreateClientForUser(Alice))).FuelEnergySpend;

        Assert.Equal(12, spend.Count);
        Assert.All(spend, month => Assert.Equal((0m, 0m), (month.Fuel, month.Charging)));
    }

    [Fact]
    public async Task RequiresASignedInUser_AndRefusesAppOnlyTokens()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClientWithToken(null).GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientForWorkflow().GetAsync("/api/dashboard")).StatusCode);
    }
}
