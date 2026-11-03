using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Insurance;
using static MyGarage.Api.Tests.Insurance.InsurancePolicyTestData;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.Insurance;

public class InsuranceEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Url(Guid vehicleId, Guid? entryId = null) =>
        $"/api/vehicles/{vehicleId}/insurance" + (entryId is null ? string.Empty : $"/{entryId}");

    [Theory]
    [InlineData(InsuranceDuration.Monthly)]
    [InlineData(InsuranceDuration.BiAnnual)]
    [InlineData(InsuranceDuration.Annual)]
    public async Task Post_Returns201WithLocationAndThePolicy(InsuranceDuration duration)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostAsync(client, vehicle.Id, Request(new DateOnly(2026, 5, 1), new DateOnly(2027, 5, 1), duration, 120.50m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var policy = await response.Content.ReadFromJsonAsync<InsurancePolicyDto>();
        Assert.NotNull(policy);
        Assert.Equal(vehicle.Id, policy.VehicleId);
        Assert.Equal(new DateOnly(2026, 5, 1), policy.StartDate);
        Assert.Equal(new DateOnly(2027, 5, 1), policy.EndDate);
        Assert.Equal(duration, policy.Duration);
        Assert.Equal(120.50m, policy.Cost);
        Assert.EndsWith(Url(vehicle.Id, policy.Id), response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Post_AcceptsPastDates_ZeroCost_AndADurationShorterThanTheSpan()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var policy = await CreateAsync(client, vehicle.Id, Request(new DateOnly(2020, 1, 1), new DateOnly(2020, 2, 1), InsuranceDuration.Annual, 0m));

        Assert.Equal(new DateOnly(2020, 1, 1), policy.StartDate);
        Assert.Equal(0m, policy.Cost);
        Assert.Equal(InsuranceDuration.Annual, policy.Duration);
    }

    [Fact]
    public async Task Post_InvalidValues_Return400KeyedByField()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostAsync(
            client, vehicle.Id, Request(new DateOnly(1899, 1, 1), new DateOnly(2101, 1, 1), (InsuranceDuration)99, -1m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        foreach (var field in new[] { "startDate", "endDate", "duration", "cost" })
        {
            Assert.Contains(problem!.Errors.Keys, key => string.Equals(key, field, StringComparison.OrdinalIgnoreCase));
        }

        Assert.Empty((await ListAsync(client, vehicle.Id)).Items);
    }

    [Fact]
    public async Task Post_EndNotAfterStart_Returns400OnEndDate()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostAsync(client, vehicle.Id, Request(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        var error = Assert.Single(problem!.Errors);
        Assert.Equal("endDate", error.Key, ignoreCase: true);
        Assert.Equal(["End date must be later than the start date."], error.Value);
    }

    [Fact]
    public async Task Post_MissingValues_Return400()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await PostAsync(client, vehicle.Id, new InsurancePolicyRequest());

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
            new { startDate = "2026-05-01", endDate = "2027-05-01", duration = "Annual", cost = 10m, ownerId = Bob, userId = Bob });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty((await ListAsync(alice, vehicle.Id)).Items);
    }

    [Fact]
    public async Task List_LatestEndDateFirst_WithPagingAndClamping()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        for (var month = 1; month <= 12; month++)
        {
            await CreateAsync(client, vehicle.Id, Request(new DateOnly(2024, 1, 1), new DateOnly(2025, month, 1)));
        }

        var first = await ListAsync(client, vehicle.Id);
        var second = await ListAsync(client, vehicle.Id, "?page=2");
        var beyond = await ListAsync(client, vehicle.Id, "?page=9&pageSize=5");

        Assert.Equal(10, first.PageSize);
        Assert.Equal(12, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(new DateOnly(2025, 12, 1), first.Items[0].EndDate);
        Assert.Equal([new DateOnly(2025, 2, 1), new DateOnly(2025, 1, 1)], second.Items.Select(item => item.EndDate));
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
    public async Task Get_ReturnsThePolicy_AndUnknownIdOrWrongVehicleIs404()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var other = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var found = await client.GetFromJsonAsync<InsurancePolicyDto>(Url(vehicle.Id, created.Id));
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

        var response = await client.PutAsJsonAsync(
            Url(vehicle.Id, created.Id), Request(new DateOnly(2027, 6, 1), new DateOnly(2028, 6, 1), InsuranceDuration.Monthly, 40m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<InsurancePolicyDto>();
        Assert.Equal(new DateOnly(2027, 6, 1), updated!.StartDate);
        Assert.Equal(new DateOnly(2028, 6, 1), updated.EndDate);
        Assert.Equal(InsuranceDuration.Monthly, updated.Duration);
        Assert.Equal(40m, updated.Cost);
        var stored = await client.GetFromJsonAsync<InsurancePolicyDto>(Url(vehicle.Id, created.Id));
        Assert.Equal(InsuranceDuration.Monthly, stored!.Duration);
    }

    [Fact]
    public async Task Put_InvalidValues_Return400AndChangeNothing()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var created = await CreateAsync(client, vehicle.Id);

        var response = await client.PutAsJsonAsync(Url(vehicle.Id, created.Id), Request(cost: -1m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var stored = await client.GetFromJsonAsync<InsurancePolicyDto>(Url(vehicle.Id, created.Id));
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
    public async Task Delete_Returns204ThenThePolicyIsGone_AndUnknownIdsAre404()
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
    public async Task AnotherUsersVehicleAndPolicies_AreNotFoundForEveryEndpoint()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        var bobVehicle = await CreateVehicleAsync(bob);
        var policy = await CreateAsync(alice, vehicle.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(vehicle.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(bob, vehicle.Id, Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(vehicle.Id, policy.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(Url(vehicle.Id, policy.Id), Request(cost: 9m))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(Url(vehicle.Id, policy.Id))).StatusCode);

        // Reaching Alice's policy through Bob's own vehicle is not found either.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Url(bobVehicle.Id, policy.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(Url(bobVehicle.Id, policy.Id), Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(Url(bobVehicle.Id, policy.Id))).StatusCode);

        var unchanged = await alice.GetFromJsonAsync<InsurancePolicyDto>(Url(vehicle.Id, policy.Id));
        Assert.Equal(policy.Cost, unchanged!.Cost);
    }

    [Fact]
    public async Task WithoutAToken_EveryEndpointReturns401()
    {
        var anonymous = factory.CreateClientWithToken(null);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(anonymous, id, new InsurancePolicyRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url(id, id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(Url(id, id), new InsurancePolicyRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync(Url(id, id))).StatusCode);
    }

    [Fact]
    public async Task DeletingTheVehicle_RemovesItsPolicies()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var policy = await CreateAsync(client, vehicle.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/vehicles/{vehicle.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(vehicle.Id, policy.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(vehicle.Id))).StatusCode);
    }
}
