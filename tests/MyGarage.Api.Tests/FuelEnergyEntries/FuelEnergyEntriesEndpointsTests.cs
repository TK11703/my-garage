using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.FuelEnergyEntries;
using static MyGarage.Api.Tests.FuelEnergyEntries.FuelEnergyEntryTestData;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.FuelEnergyEntries;

public class FuelEnergyEntriesEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Url(Guid vehicleId, Guid? entryId = null) =>
        $"/api/vehicles/{vehicleId}/fuel-energy-entries" + (entryId is null ? string.Empty : $"/{entryId}");

    [Theory]
    [InlineData(FuelEnergyKind.Fuel)]
    [InlineData(FuelEnergyKind.Energy)]
    public async Task Post_Returns201WithLocationAndTheEntry(FuelEnergyKind kind)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostAsync(client, vehicle.Id, Request(kind, unitPrice: 0.1342m, quantity: 28.5m, totalCost: 3.99m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await response.Content.ReadFromJsonAsync<FuelEnergyEntryDto>();
        Assert.NotNull(entry);
        Assert.Equal(vehicle.Id, entry.VehicleId);
        Assert.Equal(kind, entry.Kind);
        Assert.Equal(0.1342m, entry.UnitPrice);
        Assert.Equal(28.5m, entry.Quantity);
        Assert.Equal(3.99m, entry.TotalCost);
        Assert.EndsWith(Url(vehicle.Id, entry.Id), response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Post_WithoutATotal_StoresPriceTimesAmountRoundedToTheCent()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var entry = await CreateAsync(client, vehicle.Id, Request(unitPrice: 3.459m, quantity: 12.345m, totalCost: null));

        Assert.Equal(42.70m, entry.TotalCost);
    }

    [Fact]
    public async Task Post_AFreeSession_IsAccepted()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var entry = await CreateAsync(client, vehicle.Id, Request(FuelEnergyKind.Energy, unitPrice: 0m, totalCost: 0m));

        Assert.Equal(0m, entry.TotalCost);
    }

    [Fact]
    public async Task Post_InvalidValues_Return400KeyedByField()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostAsync(client, vehicle.Id, Request(date: new DateOnly(2999, 1, 1), unitPrice: -1m, quantity: 0m, totalCost: -5m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        foreach (var field in new[] { "entryDate", "unitPrice", "quantity", "totalCost" })
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

        var response = await PostAsync(client, vehicle.Id, new FuelEnergyEntryRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Equal(4, problem!.Errors.Count);
    }

    [Fact]
    public async Task Post_IgnoresOwnerIdentifiersInTheBody()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);

        var response = await bob.PostAsJsonAsync(
            Url(vehicle.Id),
            new { kind = "Fuel", entryDate = "2026-05-01", unitPrice = 3m, quantity = 1m, ownerId = Bob, userId = Bob });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty((await ListAsync(alice, vehicle.Id)).Items);
    }

    [Fact]
    public async Task List_NewestEntryDateFirst_WithPagingAndClamping()
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
        Assert.Equal(new DateOnly(2025, 12, 1), first.Items[0].EntryDate);
        Assert.Equal([new DateOnly(2025, 2, 1), new DateOnly(2025, 1, 1)], second.Items.Select(item => item.EntryDate));
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
    public async Task Get_ReturnsTheEntry_AndUnknownIdOrWrongVehicleIs404()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var other = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var found = await client.GetFromJsonAsync<FuelEnergyEntryDto>(Url(vehicle.Id, created.Id));
        var unknown = await client.GetAsync(Url(vehicle.Id, Guid.NewGuid()));
        var wrongVehicle = await client.GetAsync(Url(other.Id, created.Id));

        Assert.Equal(created.Id, found!.Id);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, wrongVehicle.StatusCode);
    }

    [Fact]
    public async Task Put_ReplacesTheValues_AndRecalculatesAnEmptyTotal()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var response = await client.PutAsJsonAsync(Url(vehicle.Id, created.Id), Request(FuelEnergyKind.Energy, new DateOnly(2026, 6, 1), 0.1342m, 28.5m, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<FuelEnergyEntryDto>();
        Assert.Equal(FuelEnergyKind.Energy, updated!.Kind);
        Assert.Equal(new DateOnly(2026, 6, 1), updated.EntryDate);
        Assert.Equal(3.82m, updated.TotalCost);
    }

    [Fact]
    public async Task Put_InvalidValues_Return400AndChangeNothing()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var response = await client.PutAsJsonAsync(Url(vehicle.Id, created.Id), Request(unitPrice: -1m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var stored = await client.GetFromJsonAsync<FuelEnergyEntryDto>(Url(vehicle.Id, created.Id));
        Assert.Equal(created.UnitPrice, stored!.UnitPrice);
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
    public async Task Delete_Returns204ThenTheEntryIsGone_AndUnknownIdsAre404()
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
    public async Task AnotherUsersVehicleAndEntries_AreNotFoundForEveryEndpoint()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var bobVehicle = await CreateVehicleAsync(bob);
        var entry = await CreateAsync(alice, vehicle.Id);
        var notFound = (await bob.GetAsync(Url(Guid.NewGuid()))).StatusCode;

        Assert.Equal(HttpStatusCode.NotFound, notFound);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(vehicle.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(bob, vehicle.Id, Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(vehicle.Id, entry.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(Url(vehicle.Id, entry.Id), Request(unitPrice: 9m))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(Url(vehicle.Id, entry.Id))).StatusCode);

        // Reaching Alice's entry through Bob's own vehicle is not found either.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(bobVehicle.Id, entry.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(Url(bobVehicle.Id, entry.Id), Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(Url(bobVehicle.Id, entry.Id))).StatusCode);

        var unchanged = await alice.GetFromJsonAsync<FuelEnergyEntryDto>(Url(vehicle.Id, entry.Id));
        Assert.Equal(entry.UnitPrice, unchanged!.UnitPrice);
    }

    [Fact]
    public async Task WithoutAToken_EveryEndpointReturns401()
    {
        var anonymous = factory.CreateClientWithToken(null);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(anonymous, id, new FuelEnergyEntryRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(id, id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(Url(id, id), new FuelEnergyEntryRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync(Url(id, id))).StatusCode);
    }

    [Fact]
    public async Task DeletingTheVehicle_RemovesItsEntries()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var entry = await CreateAsync(client, vehicle.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/vehicles/{vehicle.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(vehicle.Id, entry.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(vehicle.Id))).StatusCode);
    }
}
