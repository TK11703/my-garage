using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Inspections;
using static MyGarage.Api.Tests.Inspections.InspectionTestData;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.Inspections;

public class InspectionsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Url(Guid vehicleId, Guid? entryId = null) =>
        $"/api/vehicles/{vehicleId}/inspections" + (entryId is null ? string.Empty : $"/{entryId}");

    [Theory]
    [InlineData(InspectionType.Safety)]
    [InlineData(InspectionType.Emissions)]
    public async Task Post_Returns201WithLocationAndTheInspection(InspectionType type)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostAsync(client, vehicle.Id, Request(type, cost: 39.99m, nextDueDate: new DateOnly(2027, 5, 1)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var inspection = await response.Content.ReadFromJsonAsync<InspectionDto>();
        Assert.NotNull(inspection);
        Assert.Equal(vehicle.Id, inspection.VehicleId);
        Assert.Equal(type, inspection.Type);
        Assert.Equal(new DateOnly(2026, 5, 1), inspection.InspectionDate);
        Assert.Equal(39.99m, inspection.Cost);
        Assert.Equal(new DateOnly(2027, 5, 1), inspection.NextDueDate);
        Assert.EndsWith(Url(vehicle.Id, inspection.Id), response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Post_WithoutANextDueDate_StoresNull()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var inspection = await CreateAsync(client, vehicle.Id, Request(cost: 0m));

        Assert.Null(inspection.NextDueDate);
        Assert.Equal(0m, inspection.Cost);
    }

    [Fact]
    public async Task Post_InvalidValues_Return400KeyedByField()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostAsync(client, vehicle.Id, Request(date: new DateOnly(2999, 1, 1), cost: -1m, nextDueDate: new DateOnly(2101, 1, 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        foreach (var field in new[] { "inspectionDate", "cost", "nextDueDate" })
        {
            Assert.Contains(problem!.Errors.Keys, key => string.Equals(key, field, StringComparison.OrdinalIgnoreCase));
        }

        Assert.Empty((await ListAsync(client, vehicle.Id)).Items);
    }

    [Fact]
    public async Task Post_MissingValues_Return400()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostAsync(client, vehicle.Id, new InspectionRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Equal(3, problem!.Errors.Count);
    }

    [Fact]
    public async Task Post_IgnoresOwnerIdentifiersInTheBody()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);

        var response = await bob.PostAsJsonAsync(
            Url(vehicle.Id),
            new { type = "Safety", inspectionDate = "2026-05-01", cost = 10m, ownerId = Bob, userId = Bob });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty((await ListAsync(alice, vehicle.Id)).Items);
    }

    [Fact]
    public async Task List_NewestInspectionDateFirst_WithPagingAndClamping()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        for (var month = 1; month <= 12; month++)
        {
            await CreateAsync(client, vehicle.Id, Request(date: new DateOnly(2025, month, 1)));
        }

        var first = await ListAsync(client, vehicle.Id);
        var second = await ListAsync(client, vehicle.Id, "?page=2");
        var beyond = await ListAsync(client, vehicle.Id, "?page=9&pageSize=5");

        Assert.Equal(10, first.PageSize);
        Assert.Equal(12, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(new DateOnly(2025, 12, 1), first.Items[0].InspectionDate);
        Assert.Equal([new DateOnly(2025, 2, 1), new DateOnly(2025, 1, 1)], second.Items.Select(item => item.InspectionDate));
        Assert.Equal(3, beyond.Page);
        Assert.Equal(2, beyond.Items.Count);
    }

    [Theory]
    [InlineData("?pageSize=51")]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    public async Task List_InvalidPaging_Returns400(string query)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await client.GetAsync(Url(vehicle.Id) + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_ReturnsTheInspection_AndUnknownIdOrWrongVehicleIs404()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var other = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var found = await client.GetFromJsonAsync<InspectionDto>(Url(vehicle.Id, created.Id));
        var unknown = await client.GetAsync(Url(vehicle.Id, Guid.NewGuid()));
        var wrongVehicle = await client.GetAsync(Url(other.Id, created.Id));

        Assert.Equal(created.Id, found!.Id);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, wrongVehicle.StatusCode);
    }

    [Fact]
    public async Task Put_ReplacesTheValues_AndCanClearTheNextDueDate()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id, Request(nextDueDate: new DateOnly(2027, 5, 1)));

        var response = await client.PutAsJsonAsync(Url(vehicle.Id, created.Id), Request(InspectionType.Emissions, new DateOnly(2026, 6, 1), 40m, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<InspectionDto>();
        Assert.Equal(InspectionType.Emissions, updated!.Type);
        Assert.Equal(new DateOnly(2026, 6, 1), updated.InspectionDate);
        Assert.Equal(40m, updated.Cost);
        Assert.Null(updated.NextDueDate);
        var stored = await client.GetFromJsonAsync<InspectionDto>(Url(vehicle.Id, created.Id));
        Assert.Null(stored!.NextDueDate);
    }

    [Fact]
    public async Task Put_InvalidValues_Return400AndChangeNothing()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var response = await client.PutAsJsonAsync(Url(vehicle.Id, created.Id), Request(cost: -1m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var stored = await client.GetFromJsonAsync<InspectionDto>(Url(vehicle.Id, created.Id));
        Assert.Equal(created.Cost, stored!.Cost);
    }

    [Fact]
    public async Task Put_UnknownIdOrWrongVehicle_Returns404()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var other = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(Url(vehicle.Id, Guid.NewGuid()), Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(Url(other.Id, created.Id), Request())).StatusCode);
    }

    [Fact]
    public async Task Delete_Returns204ThenTheInspectionIsGone_AndUnknownIdsAre404()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var other = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(Url(other.Id, created.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Url(vehicle.Id, created.Id))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(vehicle.Id, created.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(Url(vehicle.Id, created.Id))).StatusCode);
    }

    [Fact]
    public async Task AnotherUsersVehicleAndInspections_AreNotFoundForEveryEndpoint()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var bobVehicle = await CreateVehicleAsync(bob);
        var inspection = await CreateAsync(alice, vehicle.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(vehicle.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(bob, vehicle.Id, Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(vehicle.Id, inspection.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(Url(vehicle.Id, inspection.Id), Request(cost: 9m))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(Url(vehicle.Id, inspection.Id))).StatusCode);

        // Reaching Alice's inspection through Bob's own vehicle is not found either.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(bobVehicle.Id, inspection.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(Url(bobVehicle.Id, inspection.Id), Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(Url(bobVehicle.Id, inspection.Id))).StatusCode);

        var unchanged = await alice.GetFromJsonAsync<InspectionDto>(Url(vehicle.Id, inspection.Id));
        Assert.Equal(inspection.Cost, unchanged!.Cost);
    }

    [Fact]
    public async Task WithoutAToken_EveryEndpointReturns401()
    {
        var anonymous = factory.CreateClientWithToken(null);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(anonymous, id, new InspectionRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(id, id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(Url(id, id), new InspectionRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync(Url(id, id))).StatusCode);
    }

    [Fact]
    public async Task DeletingTheVehicle_RemovesItsInspections()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var inspection = await CreateAsync(client, vehicle.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/vehicles/{vehicle.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(vehicle.Id, inspection.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(vehicle.Id))).StatusCode);
    }
}
