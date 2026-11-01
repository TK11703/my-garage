using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

public class VehicleChangeEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static VehicleRequest Request(string nickname = "Daily") => new() { Nickname = nickname, Make = "Ford", Model = "F-150" };

    private static async Task<VehicleDetailDto> CreateAsync(HttpClient client, VehicleRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/vehicles", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
    }

    [Fact]
    public async Task Put_ValidRequest_ReturnsTheUpdatedDetail()
    {
        var client = factory.CreateClientForUser(Alice);
        var created = await CreateAsync(client, Request("Old"));

        var response = await client.PutAsJsonAsync($"/api/vehicles/{created.Id}", new VehicleRequest
        {
            Nickname = " New ", Make = "Chevy", Model = "Silverado", Trim = "LT", Color = "Red", Mileage = 5_000, Doors = 2,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<VehicleDetailDto>();
        Assert.NotNull(updated);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("New", updated.Nickname);
        Assert.Equal("Chevy", updated.Make);
        Assert.Equal("LT", updated.Trim);
        Assert.Equal(2, updated.Doors);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt >= created.UpdatedAt);
        Assert.Equal("New", (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{created.Id}"))!.Nickname);
    }

    [Fact]
    public async Task Put_InvalidRequest_Returns400AndKeepsStoredData()
    {
        var client = factory.CreateClientForUser(Alice);
        var created = await CreateAsync(client, Request("Keep me"));

        var response = await client.PutAsJsonAsync($"/api/vehicles/{created.Id}", new VehicleRequest { Nickname = "", Make = "Ford", Model = "F-150", Doors = 9 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors.Keys, key => key.Contains("Nickname", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Keep me", (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{created.Id}"))!.Nickname);
    }

    [Fact]
    public async Task Put_UnknownVehicle_Returns404ProblemDetails()
    {
        var response = await factory.CreateClientForUser(Alice).PutAsJsonAsync($"/api/vehicles/{Guid.NewGuid()}", Request());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Put_AnotherUsersVehicle_Returns404AndChangesNothing()
    {
        var alice = factory.CreateClientForUser(Alice);
        var created = await CreateAsync(alice, Request("Alice's"));

        var response = await factory.CreateClientForUser(Bob).PutAsJsonAsync($"/api/vehicles/{created.Id}", Request("Hijacked"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Alice's", (await alice.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{created.Id}"))!.Nickname);
    }

    [Fact]
    public async Task Delete_ExistingVehicle_Returns204ThenItIsGone()
    {
        var client = factory.CreateClientForUser(Alice);
        var created = await CreateAsync(client, Request());
        await factory.InsertTagAsync(created.Id, "sunroof");

        var response = await client.DeleteAsync($"/api/vehicles/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/vehicles/{created.Id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles"))!.Items);
    }

    [Fact]
    public async Task Delete_UnknownVehicle_Returns404ProblemDetails()
    {
        var response = await factory.CreateClientForUser(Alice).DeleteAsync($"/api/vehicles/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Delete_AnotherUsersVehicle_Returns404AndKeepsIt()
    {
        var alice = factory.CreateClientForUser(Alice);
        var created = await CreateAsync(alice, Request());

        var response = await factory.CreateClientForUser(Bob).DeleteAsync($"/api/vehicles/{created.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/vehicles/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task PutAndDelete_WithoutToken_Return401()
    {
        var anonymous = factory.CreateClientWithToken(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync($"/api/vehicles/{Guid.NewGuid()}", Request())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/vehicles/{Guid.NewGuid()}")).StatusCode);
    }
}
