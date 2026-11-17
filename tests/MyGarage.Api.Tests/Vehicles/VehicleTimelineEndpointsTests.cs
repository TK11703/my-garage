using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Vehicles;
using static MyGarage.Api.Tests.Reminders.ReminderApiTestData;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.Vehicles;

public class VehicleTimelineEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Url(Guid vehicleId, string query = "") => $"/api/vehicles/{vehicleId}/timeline{query}";

    private static Task<PagedResultDto<VehicleTimelineEntryDto>> GetAsync(HttpClient client, Guid vehicleId, string query = "") =>
        client.GetFromJsonAsync<PagedResultDto<VehicleTimelineEntryDto>>(Url(vehicleId, query))!;

    private static async Task SeedAsync(HttpClient client, Guid vehicleId)
    {
        var record = await CreateRecordAsync(client, vehicleId, Today.AddDays(-30), odometer: 45_000, shop: "Corner Garage");
        await AddFlatLineItemAsync(client, vehicleId, record.Id, "Oil change", 80m);
        (await client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/fuel-energy-entries",
            new FuelEnergyEntryRequest { Kind = FuelEnergyKind.Fuel, EntryDate = Today.AddDays(-10), UnitPrice = 3m, Quantity = 10m })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/inspections",
            new InspectionRequest { Type = InspectionType.Safety, InspectionDate = Today.AddDays(-5), Cost = 25m })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/notes", new JournalNoteRequest { Category = "Trip", Body = "Drove to the coast." })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Get_ReturnsTheMergedHistoryNewestFirst()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        await SeedAsync(alice, vehicle.Id);

        var page = await GetAsync(alice, vehicle.Id);

        Assert.Equal(4, page.TotalCount);
        Assert.Equal(
            [VehicleTimelineKind.Journal, VehicleTimelineKind.Inspection, VehicleTimelineKind.FuelEnergy, VehicleTimelineKind.Service],
            page.Items.Select(item => item.Kind));
        var service = page.Items.Single(item => item.Kind == VehicleTimelineKind.Service);
        Assert.Equal("Oil change", service.Title);
        Assert.Equal("Corner Garage", service.Detail);
        Assert.Equal(80m, service.Amount);
        Assert.Equal(45_000, service.OdometerReading);
    }

    [Fact]
    public async Task Get_FiltersByKind_AndPages()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        await SeedAsync(alice, vehicle.Id);

        var fuel = await GetAsync(alice, vehicle.Id, "?kind=fuel-energy");
        var firstPage = await GetAsync(alice, vehicle.Id, "?pageSize=3");
        var past = await GetAsync(alice, vehicle.Id, "?pageSize=3&page=9");

        Assert.Equal(VehicleTimelineKind.FuelEnergy, Assert.Single(fuel.Items).Kind);
        Assert.Equal(3, firstPage.Items.Count);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(2, past.Page);
        Assert.Single(past.Items);
    }

    [Theory]
    [InlineData("?kind=fuel")]
    [InlineData("?kind=Service")]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=51")]
    public async Task Get_InvalidQuery_Returns400(string query)
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);

        var response = await alice.GetAsync(Url(vehicle.Id, query));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<ValidationProblemDetails>());
    }

    [Fact]
    public async Task Get_AnotherUsersOrUnknownVehicle_Returns404()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var bobs = await CreateVehicleAsync(bob);

        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(Url(bobs.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(Url(Guid.NewGuid()))).StatusCode);
    }

    [Fact]
    public async Task Get_AVehicleWithNoHistory_ReturnsAnEmptyPage()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);

        var page = await GetAsync(alice, vehicle.Id);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task Get_AnArchivedVehicle_IsStillReadable()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        await SeedAsync(alice, vehicle.Id);
        (await alice.PostAsync($"/api/vehicles/{vehicle.Id}/archive", null)).EnsureSuccessStatusCode();

        Assert.Equal(4, (await GetAsync(alice, vehicle.Id)).TotalCount);
    }

    [Fact]
    public async Task Get_RequiresASignedInUser_AndRefusesAppOnlyTokens()
    {
        var vehicleId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClientWithToken(null).GetAsync(Url(vehicleId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientForWorkflow().GetAsync(Url(vehicleId))).StatusCode);
    }
}
