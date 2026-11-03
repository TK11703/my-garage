using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Registrations;
using static MyGarage.Api.Tests.Registrations.RegistrationTestData;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.Registrations;

public class RegistrationsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Url(Guid vehicleId, Guid? entryId = null) =>
        $"/api/vehicles/{vehicleId}/registrations" + (entryId is null ? string.Empty : $"/{entryId}");

    [Theory]
    [InlineData("WA")]
    [InlineData("DC")]
    public async Task Post_Returns201WithLocationAndTheRegistration(string state)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostAsync(client, vehicle.Id, Request(state, 39.99m, new DateOnly(2027, 5, 1)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var registration = await response.Content.ReadFromJsonAsync<RegistrationDto>();
        Assert.NotNull(registration);
        Assert.Equal(vehicle.Id, registration.VehicleId);
        Assert.Equal(state, registration.State);
        Assert.Equal(39.99m, registration.Cost);
        Assert.Equal(new DateOnly(2027, 5, 1), registration.ExpirationDate);
        Assert.EndsWith(Url(vehicle.Id, registration.Id), response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Post_AcceptsAPastExpirationDate_AndZeroCost()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var registration = await CreateAsync(client, vehicle.Id, Request(cost: 0m, expirationDate: new DateOnly(2020, 1, 31)));

        Assert.Equal(new DateOnly(2020, 1, 31), registration.ExpirationDate);
        Assert.Equal(0m, registration.Cost);
    }

    [Fact]
    public async Task Post_InvalidValues_Return400KeyedByField()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostAsync(client, vehicle.Id, Request("ZZ", -1m, new DateOnly(2101, 1, 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        foreach (var field in new[] { "state", "cost", "expirationDate" })
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

        var response = await PostAsync(client, vehicle.Id, new RegistrationRequest());

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
            new { state = "WA", cost = 10m, expirationDate = "2027-05-01", ownerId = Bob, userId = Bob });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty((await ListAsync(alice, vehicle.Id)).Items);
    }

    [Fact]
    public async Task List_LatestExpirationFirst_WithPagingAndClamping()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        for (var month = 1; month <= 12; month++)
        {
            await CreateAsync(client, vehicle.Id, Request(expirationDate: new DateOnly(2025, month, 1)));
        }

        var first = await ListAsync(client, vehicle.Id);
        var second = await ListAsync(client, vehicle.Id, "?page=2");
        var beyond = await ListAsync(client, vehicle.Id, "?page=9&pageSize=5");

        Assert.Equal(10, first.PageSize);
        Assert.Equal(12, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(new DateOnly(2025, 12, 1), first.Items[0].ExpirationDate);
        Assert.Equal([new DateOnly(2025, 2, 1), new DateOnly(2025, 1, 1)], second.Items.Select(item => item.ExpirationDate));
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
    public async Task Get_ReturnsTheRegistration_AndUnknownIdOrWrongVehicleIs404()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var other = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var found = await client.GetFromJsonAsync<RegistrationDto>(Url(vehicle.Id, created.Id));
        var unknown = await client.GetAsync(Url(vehicle.Id, Guid.NewGuid()));
        var wrongVehicle = await client.GetAsync(Url(other.Id, created.Id));

        Assert.Equal(created.Id, found!.Id);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("application/problem+json", unknown.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, wrongVehicle.StatusCode);
    }

    [Fact]
    public async Task Put_ReplacesTheValues()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var response = await client.PutAsJsonAsync(Url(vehicle.Id, created.Id), Request("OR", 40m, new DateOnly(2028, 6, 1)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<RegistrationDto>();
        Assert.Equal("OR", updated!.State);
        Assert.Equal(40m, updated.Cost);
        Assert.Equal(new DateOnly(2028, 6, 1), updated.ExpirationDate);
        var stored = await client.GetFromJsonAsync<RegistrationDto>(Url(vehicle.Id, created.Id));
        Assert.Equal("OR", stored!.State);
    }

    [Fact]
    public async Task Put_InvalidValues_Return400AndChangeNothing()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var response = await client.PutAsJsonAsync(Url(vehicle.Id, created.Id), Request(cost: -1m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var stored = await client.GetFromJsonAsync<RegistrationDto>(Url(vehicle.Id, created.Id));
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
    public async Task Delete_Returns204ThenTheRegistrationIsGone_AndUnknownIdsAre404()
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
    public async Task AnotherUsersVehicleAndRegistrations_AreNotFoundForEveryEndpoint()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var bobVehicle = await CreateVehicleAsync(bob);
        var registration = await CreateAsync(alice, vehicle.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(vehicle.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(bob, vehicle.Id, Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(vehicle.Id, registration.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(Url(vehicle.Id, registration.Id), Request(cost: 9m))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(Url(vehicle.Id, registration.Id))).StatusCode);

        // Reaching Alice's registration through Bob's own vehicle is not found either.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(bobVehicle.Id, registration.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(Url(bobVehicle.Id, registration.Id), Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(Url(bobVehicle.Id, registration.Id))).StatusCode);

        var unchanged = await alice.GetFromJsonAsync<RegistrationDto>(Url(vehicle.Id, registration.Id));
        Assert.Equal(registration.Cost, unchanged!.Cost);
    }

    [Fact]
    public async Task WithoutAToken_EveryEndpointReturns401()
    {
        var anonymous = factory.CreateClientWithToken(null);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(anonymous, id, new RegistrationRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(id, id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(Url(id, id), new RegistrationRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync(Url(id, id))).StatusCode);
    }

    [Fact]
    public async Task DeletingTheVehicle_RemovesItsRegistrations()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var registration = await CreateAsync(client, vehicle.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/vehicles/{vehicle.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(vehicle.Id, registration.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(vehicle.Id))).StatusCode);
    }
}
