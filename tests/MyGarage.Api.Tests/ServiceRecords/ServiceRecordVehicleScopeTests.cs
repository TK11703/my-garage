using System.Net;
using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.ServiceRecords;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.ServiceRecords;

public class ServiceRecordVehicleScopeTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AnotherUsersRecord_ThroughTheCallersOwnVehicle_IsNotFoundForEveryOperation()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var aliceVehicle = await CreateVehicleAsync(alice);
        var bobVehicle = await CreateVehicleAsync(bob);
        var record = await CreateRecordAsync(alice, aliceVehicle.Id);
        var line = (await AddFlatLineItemAsync(alice, aliceVehicle.Id, record.Id)).LineItems.Single();
        var basePath = $"/api/vehicles/{bobVehicle.Id}/service-records/{record.Id}";
        var recordRequest = new ServiceRecordRequest { DateServiced = new DateOnly(2026, 1, 1), OdometerReading = 1 };
        var lineRequest = new ServiceLineItemRequest { Description = "x", FlatCost = 1m };

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(basePath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(basePath, recordRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(basePath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PostAsJsonAsync($"{basePath}/line-items", lineRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"{basePath}/line-items/{line.Id}", lineRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"{basePath}/line-items/{line.Id}")).StatusCode);

        var untouched = await alice.GetFromJsonAsync<ServiceRecordDto>($"/api/vehicles/{aliceVehicle.Id}/service-records/{record.Id}");
        Assert.Equal(["Tire rotation"], untouched!.LineItems.Select(item => item.Description));
    }

    [Fact]
    public async Task DeletingAVehicle_RemovesItsRecordsAndLeavesOtherVehiclesAlone()
    {
        var client = factory.CreateClientForUser(Alice);
        var doomed = await CreateVehicleAsync(client);
        var kept = await CreateVehicleAsync(client);
        var doomedRecord = await CreateRecordAsync(client, doomed.Id);
        await AddFlatLineItemAsync(client, doomed.Id, doomedRecord.Id);
        var keptRecord = await CreateRecordAsync(client, kept.Id);

        var response = await client.DeleteAsync($"/api/vehicles/{doomed.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/vehicles/{doomed.Id}/service-records/{doomedRecord.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/vehicles/{doomed.Id}/service-records")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/vehicles/{kept.Id}/service-records/{keptRecord.Id}")).StatusCode);
    }
}
