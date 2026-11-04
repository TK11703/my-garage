using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Vehicles;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.ServiceRecords;

public class ServiceRecordsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<PagedResultDto<ServiceRecordSummaryDto>> ListAsync(HttpClient client, Guid vehicleId, string query = "") =>
        (await client.GetFromJsonAsync<PagedResultDto<ServiceRecordSummaryDto>>($"/api/vehicles/{vehicleId}/service-records{query}"))!;

    [Fact]
    public async Task Post_Returns201WithLocationAndAnEmptyRecord()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostRecordAsync(client, vehicle.Id, new ServiceRecordRequest
        {
            DateServiced = new DateOnly(2026, 5, 1),
            OdometerReading = 45000,
            ShopOrPerson = "  Joe's Garage  ",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var record = await response.Content.ReadFromJsonAsync<ServiceRecordDto>();
        Assert.NotNull(record);
        Assert.Equal(vehicle.Id, record.VehicleId);
        Assert.Equal(new DateOnly(2026, 5, 1), record.DateServiced);
        Assert.Equal(45000, record.OdometerReading);
        Assert.Equal("Joe's Garage", record.ShopOrPerson);
        Assert.Empty(record.LineItems);
        Assert.Equal(0m, record.TotalCost);
        Assert.EndsWith($"/api/vehicles/{vehicle.Id}/service-records/{record.Id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Post_WithTax_ReturnsItAndTheTotalIncludesIt_AndPutClearsIt()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var created = await PostRecordAsync(client, vehicle.Id, new ServiceRecordRequest { DateServiced = new DateOnly(2026, 5, 1), OdometerReading = 1000, Tax = 8.25m });
        var record = (await created.Content.ReadFromJsonAsync<ServiceRecordDto>())!;
        await AddFlatLineItemAsync(client, vehicle.Id, record.Id, cost: 100m);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(8.25m, record.Tax);
        var loaded = (await client.GetFromJsonAsync<ServiceRecordDto>($"/api/vehicles/{vehicle.Id}/service-records/{record.Id}"))!;
        Assert.Equal(108.25m, loaded.TotalCost);
        Assert.Equal(108.25m, Assert.Single((await ListAsync(client, vehicle.Id)).Items).TotalCost);

        var cleared = await client.PutAsJsonAsync(
            $"/api/vehicles/{vehicle.Id}/service-records/{record.Id}",
            new ServiceRecordRequest { DateServiced = new DateOnly(2026, 5, 1), OdometerReading = 1000 });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var after = (await cleared.Content.ReadFromJsonAsync<ServiceRecordDto>())!;
        Assert.Null(after.Tax);
        Assert.Equal(100m, after.TotalCost);
    }

    [Fact]
    public async Task Post_ANegativeTax_Returns400NamingTax()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostRecordAsync(client, vehicle.Id, new ServiceRecordRequest { DateServiced = new DateOnly(2026, 5, 1), OdometerReading = 1000, Tax = -1m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(problem!.Errors.Keys, key => string.Equals(key, "tax", StringComparison.OrdinalIgnoreCase));
        Assert.Empty((await ListAsync(client, vehicle.Id)).Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_BlankShop_IsStoredAsNone(string? shop)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var record = await CreateRecordAsync(client, vehicle.Id, shop: shop);

        Assert.Null(record.ShopOrPerson);
    }

    [Fact]
    public async Task Post_InvalidValues_Return400KeyedByField()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostRecordAsync(client, vehicle.Id, new ServiceRecordRequest
        {
            DateServiced = new DateOnly(2999, 1, 1),
            OdometerReading = -5,
            ShopOrPerson = new string('x', 101),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors.Keys, key => string.Equals(key, "dateServiced", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(problem.Errors.Keys, key => string.Equals(key, "odometerReading", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(problem.Errors.Keys, key => string.Equals(key, "shopOrPerson", StringComparison.OrdinalIgnoreCase));
        Assert.Empty((await ListAsync(client, vehicle.Id)).Items);
    }

    [Fact]
    public async Task Post_MissingRequiredValues_Return400()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostRecordAsync(client, vehicle.Id, new ServiceRecordRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Equal(2, problem!.Errors.Count);
    }

    [Fact]
    public async Task Post_IgnoresOwnerIdentifiersInTheBody()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var aliceVehicle = await CreateVehicleAsync(alice);

        var response = await bob.PostAsJsonAsync(
            $"/api/vehicles/{aliceVehicle.Id}/service-records",
            new { dateServiced = "2026-05-01", odometerReading = 100, ownerId = Bob, userId = Bob });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty((await ListAsync(alice, aliceVehicle.Id)).Items);
    }

    [Fact]
    public async Task List_NewestServiceFirst_WithPaging()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        for (var month = 1; month <= 12; month++)
        {
            await CreateRecordAsync(client, vehicle.Id, new DateOnly(2025, month, 1), month * 1000);
        }

        var first = await ListAsync(client, vehicle.Id);
        var second = await ListAsync(client, vehicle.Id, "?page=2");

        Assert.Equal(10, first.PageSize);
        Assert.Equal(12, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(new DateOnly(2025, 12, 1), first.Items[0].DateServiced);
        Assert.Equal(10, first.Items.Count);
        Assert.Equal([new DateOnly(2025, 2, 1), new DateOnly(2025, 1, 1)], second.Items.Select(item => item.DateServiced));
    }

    [Fact]
    public async Task List_PageBeyondTheLast_IsClampedToTheLastPage()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await CreateRecordAsync(client, vehicle.Id);

        var page = await ListAsync(client, vehicle.Id, "?page=9&pageSize=5");

        Assert.Equal(1, page.Page);
        Assert.Single(page.Items);
    }

    [Theory]
    [InlineData("?pageSize=51")]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    public async Task List_InvalidPaging_Returns400(string query)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await client.GetAsync($"/api/vehicles/{vehicle.Id}/service-records{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task List_NeverMixesVehicles()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicleA = await CreateVehicleAsync(client);
        var vehicleB = await CreateVehicleAsync(client);
        await CreateRecordAsync(client, vehicleA.Id, odometer: 111);
        await CreateRecordAsync(client, vehicleB.Id, odometer: 222);

        var page = await ListAsync(client, vehicleA.Id);

        Assert.Equal([111], page.Items.Select(item => item.OdometerReading));
    }

    [Fact]
    public async Task AfterCreateAndDelete_TheFirstPageReflectsTheCountAndTheMostRecentDate()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await CreateRecordAsync(client, vehicle.Id, new DateOnly(2025, 1, 1));
        var latest = await CreateRecordAsync(client, vehicle.Id, new DateOnly(2026, 3, 3));
        await CreateRecordAsync(client, vehicle.Id, new DateOnly(2025, 6, 6));

        var before = await ListAsync(client, vehicle.Id);
        Assert.Equal(3, before.TotalCount);
        Assert.Equal(new DateOnly(2026, 3, 3), before.Items[0].DateServiced);

        await client.DeleteAsync($"/api/vehicles/{vehicle.Id}/service-records/{latest.Id}");

        var after = await ListAsync(client, vehicle.Id);
        Assert.Equal(2, after.TotalCount);
        Assert.Equal(new DateOnly(2025, 6, 6), after.Items[0].DateServiced);
    }

    [Fact]
    public async Task Get_ReturnsTheRecord()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateRecordAsync(client, vehicle.Id, shop: "Joe's");

        var record = await client.GetFromJsonAsync<ServiceRecordDto>($"/api/vehicles/{vehicle.Id}/service-records/{created.Id}");

        Assert.NotNull(record);
        Assert.Equal(created.Id, record.Id);
        Assert.Equal("Joe's", record.ShopOrPerson);
    }

    [Fact]
    public async Task Get_UnknownIdOrWrongVehicle_Returns404ProblemDetails()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var otherVehicle = await CreateVehicleAsync(client);
        var created = await CreateRecordAsync(client, vehicle.Id);

        var unknown = await client.GetAsync($"/api/vehicles/{vehicle.Id}/service-records/{Guid.NewGuid()}");
        var wrongVehicle = await client.GetAsync($"/api/vehicles/{otherVehicle.Id}/service-records/{created.Id}");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, wrongVehicle.StatusCode);
    }

    [Fact]
    public async Task UnknownOrAnotherUsersVehicle_Returns404ForListCreateAndGet()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var record = await CreateRecordAsync(alice, vehicle.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/vehicles/{vehicle.Id}/service-records")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/vehicles/{vehicle.Id}/service-records/{record.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await PostRecordAsync(bob, vehicle.Id, new ServiceRecordRequest { DateServiced = new DateOnly(2026, 1, 1), OdometerReading = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync($"/api/vehicles/{Guid.NewGuid()}/service-records")).StatusCode);
    }

    [Fact]
    public async Task WithoutAToken_EveryEndpointReturns401()
    {
        var anonymous = factory.CreateClientWithToken(null);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/vehicles/{id}/service-records")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostRecordAsync(anonymous, id, new ServiceRecordRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/vehicles/{id}/service-records/{id}")).StatusCode);
    }
}
