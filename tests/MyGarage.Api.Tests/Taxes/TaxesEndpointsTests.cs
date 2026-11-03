using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Taxes;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;
using static MyGarage.Api.Tests.Taxes.TaxTestData;

namespace MyGarage.Api.Tests.Taxes;

public class TaxesEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Url(Guid vehicleId, Guid? entryId = null) =>
        $"/api/vehicles/{vehicleId}/taxes" + (entryId is null ? string.Empty : $"/{entryId}");

    [Theory]
    [InlineData(TaxType.PersonalProperty, TaxRecurrence.Once)]
    [InlineData(TaxType.Lease, TaxRecurrence.Monthly)]
    [InlineData(TaxType.Purchase, TaxRecurrence.BiAnnual)]
    [InlineData(TaxType.PersonalProperty, TaxRecurrence.Annual)]
    public async Task Post_Returns201WithLocationAndTheTax(TaxType type, TaxRecurrence recurrence)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostAsync(client, vehicle.Id, Request(type, 312.40m, recurrence));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var tax = await response.Content.ReadFromJsonAsync<TaxDto>();
        Assert.NotNull(tax);
        Assert.Equal(vehicle.Id, tax.VehicleId);
        Assert.Equal(type, tax.Type);
        Assert.Equal(312.40m, tax.Amount);
        Assert.Equal(recurrence, tax.Recurrence);
        Assert.EndsWith(Url(vehicle.Id, tax.Id), response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Post_InvalidValues_Return400KeyedByField()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostAsync(client, vehicle.Id, Request((TaxType)99, 0m, (TaxRecurrence)99));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        foreach (var field in new[] { "type", "amount", "recurrence" })
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

        var response = await PostAsync(client, vehicle.Id, new TaxRequest());

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
            new { type = "Lease", amount = 10m, recurrence = "Annual", ownerId = Bob, userId = Bob });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty((await ListAsync(alice, vehicle.Id)).Items);
    }

    [Fact]
    public async Task List_NewestAddedFirst_WithPagingAndClamping()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var added = new List<Guid>();
        for (var amount = 1; amount <= 12; amount++)
        {
            added.Add((await CreateAsync(client, vehicle.Id, Request(amount: amount))).Id);
        }

        var first = await ListAsync(client, vehicle.Id);
        var second = await ListAsync(client, vehicle.Id, "?page=2");
        var beyond = await ListAsync(client, vehicle.Id, "?page=9&pageSize=5");

        Assert.Equal(10, first.PageSize);
        Assert.Equal(12, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal([.. Enumerable.Reverse(added).Take(10)], first.Items.Select(item => item.Id));
        Assert.Equal([added[1], added[0]], second.Items.Select(item => item.Id));
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
    public async Task Get_ReturnsTheTax_AndUnknownIdOrWrongVehicleIs404()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var other = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var found = await client.GetFromJsonAsync<TaxDto>(Url(vehicle.Id, created.Id));
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

        var response = await client.PutAsJsonAsync(Url(vehicle.Id, created.Id), Request(TaxType.Lease, 40m, TaxRecurrence.Monthly));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<TaxDto>();
        Assert.Equal(TaxType.Lease, updated!.Type);
        Assert.Equal(40m, updated.Amount);
        Assert.Equal(TaxRecurrence.Monthly, updated.Recurrence);
        var stored = await client.GetFromJsonAsync<TaxDto>(Url(vehicle.Id, created.Id));
        Assert.Equal(TaxRecurrence.Monthly, stored!.Recurrence);
    }

    [Fact]
    public async Task Put_InvalidValues_Return400AndChangeNothing()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var response = await client.PutAsJsonAsync(Url(vehicle.Id, created.Id), Request(amount: 0m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var stored = await client.GetFromJsonAsync<TaxDto>(Url(vehicle.Id, created.Id));
        Assert.Equal(created.Amount, stored!.Amount);
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
    public async Task Delete_Returns204ThenTheTaxIsGone_AndUnknownIdsAre404()
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
    public async Task AnotherUsersVehicleAndTaxes_AreNotFoundForEveryEndpoint()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var bobVehicle = await CreateVehicleAsync(bob);
        var tax = await CreateAsync(alice, vehicle.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(vehicle.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(bob, vehicle.Id, Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(vehicle.Id, tax.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(Url(vehicle.Id, tax.Id), Request(amount: 9m))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(Url(vehicle.Id, tax.Id))).StatusCode);

        // Reaching Alice's tax through Bob's own vehicle is not found either.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(bobVehicle.Id, tax.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(Url(bobVehicle.Id, tax.Id), Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(Url(bobVehicle.Id, tax.Id))).StatusCode);

        var unchanged = await alice.GetFromJsonAsync<TaxDto>(Url(vehicle.Id, tax.Id));
        Assert.Equal(tax.Amount, unchanged!.Amount);
    }

    [Fact]
    public async Task WithoutAToken_EveryEndpointReturns401()
    {
        var anonymous = factory.CreateClientWithToken(null);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(anonymous, id, new TaxRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(id, id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(Url(id, id), new TaxRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync(Url(id, id))).StatusCode);
    }

    [Fact]
    public async Task DeletingTheVehicle_RemovesItsTaxes()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var tax = await CreateAsync(client, vehicle.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/vehicles/{vehicle.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(vehicle.Id, tax.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(vehicle.Id))).StatusCode);
    }
}
