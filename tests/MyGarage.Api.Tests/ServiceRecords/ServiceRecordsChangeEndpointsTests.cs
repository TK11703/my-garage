using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.ServiceRecords;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.ServiceRecords;

public class ServiceRecordsChangeEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid vehicleId, Guid recordId, ServiceRecordRequest request) =>
        client.PutAsJsonAsync($"/api/vehicles/{vehicleId}/service-records/{recordId}", request);

    [Fact]
    public async Task Put_UpdatesTheRecordAndKeepsItsLineItems()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id, shop: "Old");

        var response = await PutAsync(client, vehicle.Id, record.Id, new ServiceRecordRequest
        {
            DateServiced = new DateOnly(2026, 6, 6),
            OdometerReading = 51000,
            ShopOrPerson = "  New shop ",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ServiceRecordDto>();
        Assert.NotNull(updated);
        Assert.Equal(record.Id, updated.Id);
        Assert.Equal(new DateOnly(2026, 6, 6), updated.DateServiced);
        Assert.Equal(51000, updated.OdometerReading);
        Assert.Equal("New shop", updated.ShopOrPerson);
        var reloaded = await client.GetFromJsonAsync<ServiceRecordDto>($"/api/vehicles/{vehicle.Id}/service-records/{record.Id}");
        Assert.Equal(51000, reloaded!.OdometerReading);
    }

    [Fact]
    public async Task Put_InvalidValues_Return400AndChangeNothing()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id, odometer: 777);

        var response = await PutAsync(client, vehicle.Id, record.Id, new ServiceRecordRequest
        {
            DateServiced = new DateOnly(2999, 1, 1),
            OdometerReading = -1,
            ShopOrPerson = new string('x', 101),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Equal(3, problem!.Errors.Count);
        var reloaded = await client.GetFromJsonAsync<ServiceRecordDto>($"/api/vehicles/{vehicle.Id}/service-records/{record.Id}");
        Assert.Equal(777, reloaded!.OdometerReading);
    }

    [Fact]
    public async Task Put_UnknownRecordOrWrongVehicleOrAnotherUser_Returns404()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var otherVehicle = await CreateVehicleAsync(alice);
        var record = await CreateRecordAsync(alice, vehicle.Id, odometer: 100);
        var request = new ServiceRecordRequest { DateServiced = new DateOnly(2026, 2, 2), OdometerReading = 1 };

        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(alice, vehicle.Id, Guid.NewGuid(), request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(alice, otherVehicle.Id, record.Id, request)).StatusCode);
        var asBob = await PutAsync(bob, vehicle.Id, record.Id, request);
        Assert.Equal(HttpStatusCode.NotFound, asBob.StatusCode);
        Assert.Equal("application/problem+json", asBob.Content.Headers.ContentType?.MediaType);
        var reloaded = await alice.GetFromJsonAsync<ServiceRecordDto>($"/api/vehicles/{vehicle.Id}/service-records/{record.Id}");
        Assert.Equal(100, reloaded!.OdometerReading);
    }

    [Fact]
    public async Task Delete_Returns204AndTheRecordIsGone()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id);

        var response = await client.DeleteAsync($"/api/vehicles/{vehicle.Id}/service-records/{record.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/vehicles/{vehicle.Id}/service-records/{record.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_UnknownRecordOrWrongVehicleOrAnotherUser_Returns404AndKeepsTheRecord()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var otherVehicle = await CreateVehicleAsync(alice);
        var record = await CreateRecordAsync(alice, vehicle.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"/api/vehicles/{vehicle.Id}/service-records/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"/api/vehicles/{otherVehicle.Id}/service-records/{record.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/vehicles/{vehicle.Id}/service-records/{record.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/vehicles/{vehicle.Id}/service-records/{record.Id}")).StatusCode);
    }

    [Fact]
    public async Task WithoutAToken_PutAndDeleteReturn401()
    {
        var anonymous = factory.CreateClientWithToken(null);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await PutAsync(anonymous, id, id, new ServiceRecordRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/vehicles/{id}/service-records/{id}")).StatusCode);
    }
}
