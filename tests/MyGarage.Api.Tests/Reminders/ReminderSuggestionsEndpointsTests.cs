using System.Net;
using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Reminders;
using static MyGarage.Api.Tests.Reminders.ReminderApiTestData;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.Reminders;

public class ReminderSuggestionsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string SuggestionsUrl(Guid vehicleId) => $"/api/vehicles/{vehicleId}/reminder-suggestions";

    private static Task<List<SuggestedReminderDto>> GetAsync(HttpClient client, Guid vehicleId) =>
        client.GetFromJsonAsync<List<SuggestedReminderDto>>(SuggestionsUrl(vehicleId))!;

    private static async Task ServiceAsync(HttpClient client, Guid vehicleId, int daysAgo, int odometer, string description)
    {
        var record = await CreateRecordAsync(client, vehicleId, Today.AddDays(-daysAgo), odometer);
        await AddFlatLineItemAsync(client, vehicleId, record.Id, description, 50m);
    }

    [Fact]
    public async Task Get_SuggestsARepeatedServiceWithItsIntervalAndBasis()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        await ServiceAsync(alice, vehicle.Id, 360, 40_000, "Oil change");
        await ServiceAsync(alice, vehicle.Id, 180, 45_000, "Oil change");

        var suggestion = Assert.Single(await GetAsync(alice, vehicle.Id));

        Assert.Equal("Oil change", suggestion.Name);
        Assert.Equal(5_000, suggestion.IntervalMiles);
        Assert.Equal(6, suggestion.IntervalMonths);
        Assert.Equal(Today.AddDays(-180), suggestion.LastDoneOn);
        Assert.Equal(45_000, suggestion.LastDoneMileage);
        Assert.StartsWith("Done 2 times; last on ", suggestion.Basis);
    }

    [Fact]
    public async Task Get_LeavesOutAServiceThatAlreadyHasAReminder()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        await ServiceAsync(alice, vehicle.Id, 360, 40_000, "Oil change");
        await ServiceAsync(alice, vehicle.Id, 180, 45_000, "Oil change");
        await CreateAsync(alice, vehicle.Id, Request("oil change"));

        Assert.Empty(await GetAsync(alice, vehicle.Id));
    }

    [Fact]
    public async Task Get_AVehicleWithoutRepeatedServices_ReturnsAnEmptyList()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        await ServiceAsync(alice, vehicle.Id, 100, 40_000, "Brake pads");

        Assert.Empty(await GetAsync(alice, vehicle.Id));
    }

    [Fact]
    public async Task Get_AnArchivedVehicle_ReturnsAnEmptyList()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        await ServiceAsync(alice, vehicle.Id, 360, 40_000, "Oil change");
        await ServiceAsync(alice, vehicle.Id, 180, 45_000, "Oil change");
        (await alice.PostAsync($"/api/vehicles/{vehicle.Id}/archive", null)).EnsureSuccessStatusCode();

        var response = await alice.GetAsync(SuggestionsUrl(vehicle.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<SuggestedReminderDto>>())!);
    }

    [Fact]
    public async Task Get_AnotherUsersOrUnknownVehicle_Returns404()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var bobs = await CreateVehicleAsync(bob);
        await ServiceAsync(bob, bobs.Id, 360, 40_000, "Oil change");
        await ServiceAsync(bob, bobs.Id, 180, 45_000, "Oil change");

        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(SuggestionsUrl(bobs.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(SuggestionsUrl(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task Get_RequiresASignedInUser_AndRefusesAppOnlyTokens()
    {
        var vehicleId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClientWithToken(null).GetAsync(SuggestionsUrl(vehicleId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientForWorkflow().GetAsync(SuggestionsUrl(vehicleId))).StatusCode);
    }
}
