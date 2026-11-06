using System.Net;
using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Dashboard;
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
    public async Task RequiresASignedInUser_AndRefusesAppOnlyTokens()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClientWithToken(null).GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientForWorkflow().GetAsync("/api/dashboard")).StatusCode);
    }
}
