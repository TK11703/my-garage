using System.Net;
using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Reminders;
using static MyGarage.Api.Tests.Reminders.ReminderApiTestData;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.Reminders;

public class GarageRemindersEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<List<ReminderDto>> ListAllAsync(HttpClient client, string query = "") =>
        client.GetFromJsonAsync<List<ReminderDto>>("/api/reminders" + query)!;

    // Alice: vehicle one (44,800 miles) with Oil (due soon), Inspection (overdue), Registration (upcoming); vehicle two with no mileage and Tires.
    private async Task<(Guid One, Guid Two)> SeedAsync(HttpClient alice)
    {
        var one = await CreateVehicleAsync(alice);
        var two = await CreateVehicleAsync(alice);
        await CreateRecordAsync(alice, one.Id, new DateOnly(2026, 6, 1), odometer: 44_800);
        await CreateAsync(alice, one.Id, Request("Oil change"));
        await CreateAsync(alice, one.Id, Request("Inspection", miles: null, time: Time(1, ReminderTimeUnit.Years), lastDoneOn: Today.AddDays(-400), lastDoneMileage: null));
        await CreateAsync(alice, one.Id, Request("Registration", miles: null, time: Time(1, ReminderTimeUnit.Years), lastDoneOn: Today.AddDays(-10), lastDoneMileage: null));
        await CreateAsync(alice, two.Id, Request("Tire rotation", miles: 7_500, lastDoneMileage: 1_000));
        return (one.Id, two.Id);
    }

    [Fact]
    public async Task Get_ReturnsEveryReminderAcrossVehicles_MostUrgentFirst_EachWithItsVehicle()
    {
        var alice = factory.CreateClientForUser(Alice);
        var (one, two) = await SeedAsync(alice);

        var list = await ListAllAsync(alice);

        Assert.Equal(["Inspection", "Oil change", "Tire rotation", "Registration"], list.Select(item => item.Name));
        Assert.Equal([ReminderStatus.Overdue, ReminderStatus.DueSoon, ReminderStatus.MileageNeeded, ReminderStatus.Upcoming], list.Select(item => item.Status));
        Assert.Equal([one, one, two, one], list.Select(item => item.VehicleId));
        Assert.All(list, item => Assert.Equal("2020 Ford F-150", item.VehicleName));
    }

    [Fact]
    public async Task Get_NeverReturnsAnotherUsersReminders()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        await SeedAsync(alice);
        var bobsVehicle = await CreateVehicleAsync(bob);
        var bobs = await CreateAsync(bob, bobsVehicle.Id, Request("Bob's oil"));

        Assert.Equal([bobs.Id], (await ListAllAsync(bob)).Select(item => item.Id));
        Assert.DoesNotContain(await ListAllAsync(alice), item => item.Id == bobs.Id);
    }

    [Theory]
    [InlineData("overdue", new[] { "Inspection" })]
    [InlineData("due_soon", new[] { "Oil change" })]
    [InlineData("mileage_needed", new[] { "Tire rotation" })]
    [InlineData("upcoming", new[] { "Registration" })]
    [InlineData("paused", new string[0])]
    public async Task Get_FiltersByTheComputedStatus(string status, string[] expected)
    {
        var alice = factory.CreateClientForUser(Alice);
        await SeedAsync(alice);

        var list = await ListAllAsync(alice, $"?status={status}");

        Assert.Equal(expected, list.Select(item => item.Name));
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("Overdue ")]
    [InlineData("1")]
    public async Task Get_AnUnknownStatus_Returns400(string status)
    {
        var alice = factory.CreateClientForUser(Alice);

        var response = await alice.GetAsync($"/api/reminders?status={Uri.EscapeDataString(status)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_AnEmptyGarage_ReturnsAnEmptyArray()
    {
        var alice = factory.CreateClientForUser(Alice);

        Assert.Empty(await ListAllAsync(alice));
        await CreateVehicleAsync(alice);
        Assert.Empty(await ListAllAsync(alice));
    }

    [Fact]
    public async Task Get_RequiresASignedInUser_AndRefusesAppOnlyTokens()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClientWithToken(null).GetAsync("/api/reminders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientForWorkflow().GetAsync("/api/reminders")).StatusCode);
    }

    [Fact]
    public async Task TheVehicleList_IsAlsoMostUrgentFirst()
    {
        var alice = factory.CreateClientForUser(Alice);
        var (one, _) = await SeedAsync(alice);

        var list = await ListAsync(alice, one);

        Assert.Equal(["Inspection", "Oil change", "Registration"], list.Select(item => item.Name));
    }
}
