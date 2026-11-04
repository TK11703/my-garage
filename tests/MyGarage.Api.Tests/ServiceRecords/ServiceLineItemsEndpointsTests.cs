using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.ServiceRecords;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.ServiceRecords;

public class ServiceLineItemsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static ServiceLineItemRequest OilChange() =>
        new()
        {
            Description = "Oil change",
            LaborHours = 1.5m,
            LaborRate = 90m,
            Parts =
            [
                new ServicePartRequest { Name = "Oil filter", Quantity = 1m, UnitPrice = 12.50m },
                new ServicePartRequest { Name = "Oil", Quantity = 5m, UnitPrice = 8.25m },
            ],
        };

    private static Task<HttpResponseMessage> PutLineAsync(HttpClient client, Guid vehicleId, Guid recordId, Guid lineId, ServiceLineItemRequest request) =>
        client.PutAsJsonAsync($"/api/vehicles/{vehicleId}/service-records/{recordId}/line-items/{lineId}", request);

    private static Task<HttpResponseMessage> DeleteLineAsync(HttpClient client, Guid vehicleId, Guid recordId, Guid lineId) =>
        client.DeleteAsync($"/api/vehicles/{vehicleId}/service-records/{recordId}/line-items/{lineId}");

    private static async Task<ServiceRecordDto> GetRecordAsync(HttpClient client, Guid vehicleId, Guid recordId) =>
        (await client.GetFromJsonAsync<ServiceRecordDto>($"/api/vehicles/{vehicleId}/service-records/{recordId}"))!;

    [Fact]
    public async Task Post_FlatCost_Returns201WithTheWholeRecordAndTheNewTotal()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id);

        var response = await PostLineItemAsync(client, vehicle.Id, record.Id, new ServiceLineItemRequest { Description = "  Tire rotation ", FlatCost = 40m });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ServiceRecordDto>();
        Assert.NotNull(updated);
        var line = Assert.Single(updated.LineItems);
        Assert.Equal("Tire rotation", line.Description);
        Assert.Equal(40m, line.FlatCost);
        Assert.Equal(40m, line.TotalCost);
        Assert.Equal(40m, updated.TotalCost);
    }

    [Fact]
    public async Task Post_ADiscountLine_ReducesTheRecordTotal_AndAnOutOfRangeOneIsRejected()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id);
        await AddFlatLineItemAsync(client, vehicle.Id, record.Id, "Oil change", 50m);

        var response = await PostLineItemAsync(client, vehicle.Id, record.Id, new ServiceLineItemRequest { Description = "Coupon", FlatCost = -4.04m });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ServiceRecordDto>())!;
        var coupon = updated.LineItems.Single(line => line.Description == "Coupon");
        Assert.Equal(-4.04m, coupon.FlatCost);
        Assert.Equal(-4.04m, coupon.TotalCost);
        Assert.Equal(45.96m, updated.TotalCost);

        var tooLow = await PostLineItemAsync(client, vehicle.Id, record.Id, new ServiceLineItemRequest { Description = "Too much", FlatCost = -10_000_000m });
        Assert.Equal(HttpStatusCode.BadRequest, tooLow.StatusCode);
        var problem = await tooLow.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(problem!.Errors.Keys, key => key.Equals("flatCost", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Post_ALaborTotalOnlyLine_StoresAndReturnsTheEnteredAmount_AndWithPartsTotalsBoth()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id);

        var labor = await PostLineItemAsync(client, vehicle.Id, record.Id, new ServiceLineItemRequest { Description = "Labor", LaborCost = 180m });
        var withParts = await PostLineItemAsync(
            client, vehicle.Id, record.Id,
            new ServiceLineItemRequest { Description = "Brake job", LaborCost = 180m, Parts = [new ServicePartRequest { Name = "Pads", Quantity = 1m, UnitPrice = 25m }] });

        Assert.Equal(HttpStatusCode.Created, labor.StatusCode);
        Assert.Equal(HttpStatusCode.Created, withParts.StatusCode);
        var updated = (await withParts.Content.ReadFromJsonAsync<ServiceRecordDto>())!;
        var first = updated.LineItems.Single(line => line.Description == "Labor");
        Assert.Equal(180m, first.LaborCost);
        Assert.Null(first.LaborHours);
        Assert.Equal(180m, first.TotalCost);
        Assert.Equal(205m, updated.LineItems.Single(line => line.Description == "Brake job").TotalCost);
        Assert.Equal(385m, updated.TotalCost);
    }

    [Fact]
    public async Task Post_HoursAndRateWithATypedLaborTotal_UseTheCalculation()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id);

        var response = await PostLineItemAsync(
            client, vehicle.Id, record.Id, new ServiceLineItemRequest { Description = "Labor", LaborHours = 2.5m, LaborRate = 120m, LaborCost = 999.99m });

        var line = Assert.Single((await response.Content.ReadFromJsonAsync<ServiceRecordDto>())!.LineItems);
        Assert.Equal(300m, line.LaborCost);
        Assert.Equal(300m, line.TotalCost);
    }

    [Fact]
    public async Task Post_HoursWithoutARate_Returns400()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id);

        var response = await PostLineItemAsync(client, vehicle.Id, record.Id, new ServiceLineItemRequest { Description = "Labor", LaborHours = 2m, LaborCost = 180m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(problem!.Errors.Keys, key => key.Equals("laborRate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Post_BreakdownAndTheRunningTotal()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id);
        await AddFlatLineItemAsync(client, vehicle.Id, record.Id, cost: 40m);

        var response = await PostLineItemAsync(client, vehicle.Id, record.Id, OilChange());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ServiceRecordDto>())!;
        var oil = updated.LineItems.Single(line => line.Description == "Oil change");
        Assert.Equal(135m, oil.LaborCost);
        Assert.Equal([12.50m, 41.25m], oil.Parts.Select(part => part.TotalPrice));
        Assert.Equal(188.75m, oil.TotalCost);
        Assert.Equal(228.75m, updated.TotalCost);
        Assert.Equal(["Tire rotation", "Oil change"], updated.LineItems.Select(line => line.Description));
    }

    [Fact]
    public async Task Post_InvalidValues_Return400KeyedByFieldAndStoreNothing()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id);

        var response = await PostLineItemAsync(client, vehicle.Id, record.Id, new ServiceLineItemRequest
        {
            Description = "",
            FlatCost = 10m,
            LaborHours = 1m,
            LaborRate = 10m,
            Parts = [new ServicePartRequest { Name = "", Quantity = 0m, UnitPrice = -1m }],
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors.Keys, key => key.Equals("description", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(problem.Errors.Keys, key => key.Equals("pricing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(problem.Errors.Keys, key => key.StartsWith("parts[0].quantity", StringComparison.OrdinalIgnoreCase));
        Assert.Empty((await GetRecordAsync(client, vehicle.Id, record.Id)).LineItems);
    }

    [Fact]
    public async Task Post_AFlatCostWithLabor_IsRejectedAsBoth()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id);

        var response = await PostLineItemAsync(client, vehicle.Id, record.Id,
            new ServiceLineItemRequest { Description = "x", FlatCost = 5m, LaborHours = 1m, LaborRate = 5m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("Use either a flat cost or labor and parts, not both.", problem!.Errors.Values.SelectMany(messages => messages));
    }

    [Fact]
    public async Task Post_TheFiftyFirstLineItem_Returns409ProblemDetails()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id);
        for (var i = 0; i < 50; i++)
        {
            await AddFlatLineItemAsync(client, vehicle.Id, record.Id, $"Line {i}", 1m);
        }

        var response = await PostLineItemAsync(client, vehicle.Id, record.Id, new ServiceLineItemRequest { Description = "One too many", FlatCost = 1m });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("A service record can have up to 50 line items.", problem!.Title);
        Assert.Equal(50, (await GetRecordAsync(client, vehicle.Id, record.Id)).LineItems.Count);
    }

    [Fact]
    public async Task Post_UnknownRecordWrongVehicleOrAnotherUser_Returns404()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var otherVehicle = await CreateVehicleAsync(alice);
        var record = await CreateRecordAsync(alice, vehicle.Id);
        var request = new ServiceLineItemRequest { Description = "x", FlatCost = 1m };

        Assert.Equal(HttpStatusCode.NotFound, (await PostLineItemAsync(alice, vehicle.Id, Guid.NewGuid(), request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostLineItemAsync(alice, otherVehicle.Id, record.Id, request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostLineItemAsync(bob, vehicle.Id, record.Id, request)).StatusCode);
        Assert.Empty((await GetRecordAsync(alice, vehicle.Id, record.Id)).LineItems);
    }

    [Fact]
    public async Task Put_SwitchesFlatToBreakdownAndBackAndTheTotalFollows()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id);
        var line = (await AddFlatLineItemAsync(client, vehicle.Id, record.Id, "Service", 40m)).LineItems.Single();

        var toBreakdown = await PutLineAsync(client, vehicle.Id, record.Id, line.Id, OilChange());
        Assert.Equal(HttpStatusCode.OK, toBreakdown.StatusCode);
        var asBreakdown = (await toBreakdown.Content.ReadFromJsonAsync<ServiceRecordDto>())!;
        Assert.Equal(188.75m, asBreakdown.TotalCost);
        Assert.Null(asBreakdown.LineItems.Single().FlatCost);

        var toFlat = await PutLineAsync(client, vehicle.Id, record.Id, line.Id, new ServiceLineItemRequest { Description = "Flat again", FlatCost = 15m });
        var asFlat = (await toFlat.Content.ReadFromJsonAsync<ServiceRecordDto>())!;
        Assert.Equal(15m, asFlat.TotalCost);
        Assert.Empty(asFlat.LineItems.Single().Parts);
        Assert.Null(asFlat.LineItems.Single().LaborHours);
    }

    [Fact]
    public async Task Put_InvalidValues_Return400AndChangeNothing()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id);
        var line = (await AddFlatLineItemAsync(client, vehicle.Id, record.Id, "Keep me", 40m)).LineItems.Single();

        var response = await PutLineAsync(client, vehicle.Id, record.Id, line.Id, new ServiceLineItemRequest { Description = "", FlatCost = -1m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Keep me", (await GetRecordAsync(client, vehicle.Id, record.Id)).LineItems.Single().Description);
    }

    [Fact]
    public async Task Put_UnknownLineOrAnotherRecordsLineOrAnotherUser_Returns404()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var record = await CreateRecordAsync(alice, vehicle.Id);
        var otherRecord = await CreateRecordAsync(alice, vehicle.Id, odometer: 5);
        var line = (await AddFlatLineItemAsync(alice, vehicle.Id, record.Id)).LineItems.Single();
        var request = new ServiceLineItemRequest { Description = "x", FlatCost = 1m };

        Assert.Equal(HttpStatusCode.NotFound, (await PutLineAsync(alice, vehicle.Id, record.Id, Guid.NewGuid(), request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutLineAsync(alice, vehicle.Id, otherRecord.Id, line.Id, request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutLineAsync(bob, vehicle.Id, record.Id, line.Id, request)).StatusCode);
        Assert.Equal("Tire rotation", (await GetRecordAsync(alice, vehicle.Id, record.Id)).LineItems.Single().Description);
    }

    [Fact]
    public async Task Delete_Returns204AndTheTotalFollows()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id);
        await AddFlatLineItemAsync(client, vehicle.Id, record.Id, "Keep", 40m);
        var withBoth = await PostLineItemAsync(client, vehicle.Id, record.Id, OilChange());
        var oil = (await withBoth.Content.ReadFromJsonAsync<ServiceRecordDto>())!.LineItems.Single(line => line.Description == "Oil change");

        var response = await DeleteLineAsync(client, vehicle.Id, record.Id, oil.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var reloaded = await GetRecordAsync(client, vehicle.Id, record.Id);
        Assert.Equal(["Keep"], reloaded.LineItems.Select(line => line.Description));
        Assert.Equal(40m, reloaded.TotalCost);
    }

    [Fact]
    public async Task Delete_UnknownLineOrAnotherUser_Returns404AndKeepsTheLine()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var record = await CreateRecordAsync(alice, vehicle.Id);
        var line = (await AddFlatLineItemAsync(alice, vehicle.Id, record.Id)).LineItems.Single();

        Assert.Equal(HttpStatusCode.NotFound, (await DeleteLineAsync(alice, vehicle.Id, record.Id, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteLineAsync(bob, vehicle.Id, record.Id, line.Id)).StatusCode);
        Assert.Single((await GetRecordAsync(alice, vehicle.Id, record.Id)).LineItems);
    }

    [Fact]
    public async Task WithoutAToken_EveryLineItemEndpointReturns401()
    {
        var anonymous = factory.CreateClientWithToken(null);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await PostLineItemAsync(anonymous, id, id, new ServiceLineItemRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PutLineAsync(anonymous, id, id, id, new ServiceLineItemRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await DeleteLineAsync(anonymous, id, id, id)).StatusCode);
    }

    [Fact]
    public async Task APartIdOrOwnerInTheBody_IsIgnored()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/vehicles/{vehicle.Id}/service-records/{record.Id}/line-items",
            new
            {
                description = "Pads",
                ownerId = Bob,
                parts = new[] { new { id = Guid.NewGuid(), name = "Pad", quantity = 2, unitPrice = 10, ownerId = Bob } },
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ServiceRecordDto>())!;
        Assert.Equal(20m, updated.TotalCost);
    }
}
