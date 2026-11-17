using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.Vehicles;
using static MyGarage.Api.Tests.Reminders.ReminderApiTestData;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.Vehicles;

public class VehicleReadoutsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<VehicleSummaryDto> SummaryAsync(HttpClient client, Guid vehicleId, string query = "?status=all") =>
        (await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>($"/api/vehicles{query}"))!.Items.Single(item => item.Id == vehicleId);

    private static Task<HttpResponseMessage> AddFuelAsync(HttpClient client, Guid vehicleId, DateOnly date, decimal total) =>
        client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/fuel-energy-entries",
            new FuelEnergyEntryRequest { Kind = FuelEnergyKind.Fuel, EntryDate = date, UnitPrice = 1m, Quantity = total, TotalCost = total });

    [Fact]
    public async Task NextDue_IsTheMostUrgentActiveReminder()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        await CreateRecordAsync(alice, vehicle.Id, Today.AddDays(-5), odometer: 44_800);
        await CreateAsync(alice, vehicle.Id, Request("Registration", miles: null, time: Time(1, ReminderTimeUnit.Years), lastDoneOn: Today.AddDays(-10), lastDoneMileage: null));
        await CreateAsync(alice, vehicle.Id, Request("Oil change"));
        var overdue = await CreateAsync(alice, vehicle.Id, Request("Inspection", miles: null, time: Time(1, ReminderTimeUnit.Years), lastDoneOn: Today.AddDays(-400), lastDoneMileage: null));

        var nextDue = (await SummaryAsync(alice, vehicle.Id)).NextDue;

        Assert.NotNull(nextDue);
        Assert.Equal(overdue.Id, nextDue.ReminderId);
        Assert.Equal("Inspection", nextDue.Name);
        Assert.Equal(ReminderStatus.Overdue, nextDue.Status);
        Assert.True(nextDue.DaysRemaining < 0);
        Assert.Null(nextDue.MilesRemaining);
    }

    [Fact]
    public async Task NextDue_CarriesMilesRemaining()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        await CreateRecordAsync(alice, vehicle.Id, Today.AddDays(-5), odometer: 44_800);
        await CreateAsync(alice, vehicle.Id, Request("Oil change"));

        var nextDue = (await SummaryAsync(alice, vehicle.Id)).NextDue;

        Assert.Equal(200, nextDue!.MilesRemaining);
        Assert.Equal(ReminderStatus.DueSoon, nextDue.Status);
    }

    [Fact]
    public async Task NextDue_IsNullWithoutRemindersOrWhenAllArePaused()
    {
        var alice = factory.CreateClientForUser(Alice);
        var none = await CreateVehicleAsync(alice);
        var paused = await CreateVehicleAsync(alice);
        var reminder = await CreateAsync(alice, paused.Id);
        var response = await alice.PostAsync(Url(paused.Id, reminder.Id, "pause"), null);
        response.EnsureSuccessStatusCode();

        Assert.Null((await SummaryAsync(alice, none.Id)).NextDue);
        Assert.Null((await SummaryAsync(alice, paused.Id)).NextDue);
    }

    [Fact]
    public async Task NextDue_IsNullForAnArchivedVehicle()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        await CreateAsync(alice, vehicle.Id);
        (await alice.PostAsync($"/api/vehicles/{vehicle.Id}/archive", null)).EnsureSuccessStatusCode();

        Assert.Null((await SummaryAsync(alice, vehicle.Id)).NextDue);
    }

    [Fact]
    public async Task NextDue_NeverShowsAnotherUsersReminder()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var bobs = await CreateVehicleAsync(bob);
        await CreateAsync(bob, bobs.Id);
        var mine = await CreateVehicleAsync(alice);

        Assert.Null((await SummaryAsync(alice, mine.Id)).NextDue);
        Assert.NotNull((await SummaryAsync(bob, bobs.Id)).NextDue);
    }

    [Fact]
    public async Task SpendThisYear_SumsServiceFuelAndInspectionCostsInTheCurrentYear()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        var inYear = new DateOnly(Today.Year, 1, 1);
        var record = await CreateRecordAsync(alice, vehicle.Id, inYear);
        await AddFlatLineItemAsync(alice, vehicle.Id, record.Id, cost: 60m);
        (await AddFuelAsync(alice, vehicle.Id, inYear, 40m)).EnsureSuccessStatusCode();
        (await alice.PostAsJsonAsync($"/api/vehicles/{vehicle.Id}/inspections",
            new InspectionRequest { Type = InspectionType.Safety, InspectionDate = inYear, Cost = 25m })).EnsureSuccessStatusCode();

        Assert.Equal(125m, (await SummaryAsync(alice, vehicle.Id)).SpendThisYear);
    }

    [Fact]
    public async Task SpendThisYear_IgnoresLastYear_AndIsNullWhenNothingWasSpent()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        (await AddFuelAsync(alice, vehicle.Id, new DateOnly(Today.Year - 1, 12, 31), 500m)).EnsureSuccessStatusCode();

        Assert.Null((await SummaryAsync(alice, vehicle.Id)).SpendThisYear);
    }

    [Fact]
    public async Task SpendThisYear_IsOwnerScoped()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var mine = await CreateVehicleAsync(alice);
        var bobs = await CreateVehicleAsync(bob);
        (await AddFuelAsync(bob, bobs.Id, new DateOnly(Today.Year, 1, 2), 900m)).EnsureSuccessStatusCode();

        Assert.Null((await SummaryAsync(alice, mine.Id)).SpendThisYear);
        Assert.Equal(900m, (await SummaryAsync(bob, bobs.Id)).SpendThisYear);
    }

    [Fact]
    public async Task TheDetail_CarriesTheSameOdometerAsTheCard_TheLastServiceDate_AndTheSpend()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        var serviceDate = new DateOnly(Today.Year, 1, 5);
        var record = await CreateRecordAsync(alice, vehicle.Id, serviceDate, odometer: 44_800);
        await AddFlatLineItemAsync(alice, vehicle.Id, record.Id, cost: 60m);
        await CreateRecordAsync(alice, vehicle.Id, new DateOnly(Today.Year - 1, 1, 5), odometer: 50_000);

        var detail = (await alice.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!;
        var card = await SummaryAsync(alice, vehicle.Id);

        Assert.Equal(44_800, detail.CurrentMileage);
        Assert.Equal(card.Mileage, detail.CurrentMileage);
        Assert.Equal(serviceDate, detail.LastServiceOn);
        Assert.Equal(60m, detail.SpendThisYear);
    }

    [Fact]
    public async Task TheDetail_OfAVehicleWithNothingRecorded_HasNoReadouts()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);

        var detail = (await alice.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!;

        Assert.Null(detail.CurrentMileage);
        Assert.Null(detail.LastServiceOn);
        Assert.Null(detail.SpendThisYear);
    }

    [Fact]
    public async Task TheDetailSpend_NeverIncludesAnotherVehicleOrUser()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var mine = await CreateVehicleAsync(alice);
        var other = await CreateVehicleAsync(alice);
        var bobs = await CreateVehicleAsync(bob);
        (await AddFuelAsync(alice, mine.Id, new DateOnly(Today.Year, 1, 2), 10m)).EnsureSuccessStatusCode();
        (await AddFuelAsync(alice, other.Id, new DateOnly(Today.Year, 1, 2), 500m)).EnsureSuccessStatusCode();
        (await AddFuelAsync(bob, bobs.Id, new DateOnly(Today.Year, 1, 2), 900m)).EnsureSuccessStatusCode();

        Assert.Equal(10m, (await alice.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{mine.Id}"))!.SpendThisYear);
    }

    [Fact]
    public async Task TheSearchResults_CarryTheSameReadouts()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        await CreateAsync(alice, vehicle.Id);
        (await AddFuelAsync(alice, vehicle.Id, new DateOnly(Today.Year, 1, 2), 30m)).EnsureSuccessStatusCode();

        var found = await SummaryAsync(alice, vehicle.Id, "?q=F-150");

        Assert.NotNull(found.NextDue);
        Assert.Equal(30m, found.SpendThisYear);
    }
}
