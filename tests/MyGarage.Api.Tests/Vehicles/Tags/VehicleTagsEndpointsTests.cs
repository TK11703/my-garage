using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles.Tags;

public class VehicleTagsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<VehicleDetailDto> CreateVehicleAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020 });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
    }

    private static Task<HttpResponseMessage> AddTagAsync(HttpClient client, Guid vehicleId, string? name) =>
        client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/tags", new AddVehicleTagRequest { Name = name });

    [Fact]
    public async Task Post_ValidTag_Returns201AndTheTagAppearsOnTheVehicle()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await AddTagAsync(client, vehicle.Id, "  Sunroof ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var tag = await response.Content.ReadFromJsonAsync<VehicleTagDto>();
        Assert.NotNull(tag);
        Assert.Equal("Sunroof", tag.Name);
        var detail = await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}");
        Assert.Equal([tag.Id], detail!.Tags.Select(t => t.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("0123456789012345678901234567890")]
    public async Task Post_InvalidName_Returns400ValidationProblem(string? name)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await AddTagAsync(client, vehicle.Id, name);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(problem!.Errors.Keys, key => key.Contains("Name", StringComparison.OrdinalIgnoreCase));
        Assert.All(problem.Errors.Values.SelectMany(messages => messages), message => Assert.StartsWith("Feature ", message, StringComparison.Ordinal));
        Assert.Empty((await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!.Tags);
    }

    [Fact]
    public async Task Post_DuplicateIgnoringCaseAndSpaces_Returns409()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await AddTagAsync(client, vehicle.Id, "sunroof");

        var response = await AddTagAsync(client, vehicle.Id, " SUNROOF ");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("This vehicle already has that feature.", problem!.Title);
        Assert.Single((await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!.Tags);
    }

    [Fact]
    public async Task Post_OneTagOverTheLimit_Returns409()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        for (var i = 1; i <= MyGarage.Database.Tags.VehicleTagRepository.MaxTagsPerVehicle; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await AddTagAsync(client, vehicle.Id, $"tag {i}")).StatusCode);
        }

        var response = await AddTagAsync(client, vehicle.Id, "one more");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal($"A vehicle can have at most {MyGarage.Database.Tags.VehicleTagRepository.MaxTagsPerVehicle} features.", problem!.Title);
        Assert.Equal(MyGarage.Database.Tags.VehicleTagRepository.MaxTagsPerVehicle, (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!.Tags.Count);
    }

    [Fact]
    public async Task Post_UnknownOrAnotherUsersVehicle_Returns404()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);

        Assert.Equal(HttpStatusCode.NotFound, (await AddTagAsync(alice, Guid.NewGuid(), "x")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AddTagAsync(factory.CreateClientForUser(Bob), vehicle.Id, "x")).StatusCode);
        Assert.Empty((await alice.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!.Tags);
    }

    [Fact]
    public async Task Delete_ExistingTag_Returns204AndOnlyThatTagIsRemoved()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var keep = (await (await AddTagAsync(client, vehicle.Id, "keep")).Content.ReadFromJsonAsync<VehicleTagDto>())!;
        var drop = (await (await AddTagAsync(client, vehicle.Id, "drop")).Content.ReadFromJsonAsync<VehicleTagDto>())!;

        var response = await client.DeleteAsync($"/api/vehicles/{vehicle.Id}/tags/{drop.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([keep.Id], (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!.Tags.Select(t => t.Id));
    }

    [Fact]
    public async Task Post_TheSameFeatureOnTwoOfTheSameUsersVehicles_IsAllowedAndSharesOneCatalogEntry()
    {
        var client = factory.CreateClientForUser(Alice);
        var first = await CreateVehicleAsync(client);
        var second = await CreateVehicleAsync(client);

        Assert.Equal(HttpStatusCode.Created, (await AddTagAsync(client, first.Id, "Sunroof")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AddTagAsync(client, second.Id, "sunroof")).StatusCode);

        Assert.Equal(1, await factory.CountFeaturesAsync());
    }

    [Fact]
    public async Task Post_SameFeatureInDifferentCaseOnAnotherUsersVehicle_ReusesOneCatalogRowAndReturnsTheCanonicalCasing()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var aliceVehicle = await CreateVehicleAsync(alice);
        var bobVehicle = await CreateVehicleAsync(bob);

        var first = await AddTagAsync(alice, aliceVehicle.Id, "Heated Seats");
        var second = await AddTagAsync(bob, bobVehicle.Id, "  HEATED seats ");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstTag = (await first.Content.ReadFromJsonAsync<VehicleTagDto>())!;
        var secondTag = (await second.Content.ReadFromJsonAsync<VehicleTagDto>())!;
        Assert.Equal("Heated Seats", firstTag.Name);
        Assert.Equal("Heated Seats", secondTag.Name);
        Assert.NotEqual(firstTag.Id, secondTag.Id);
        Assert.Equal(1, await factory.CountFeaturesAsync());
        Assert.Equal(["Heated Seats"], (await bob.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{bobVehicle.Id}"))!.Tags.Select(t => t.Name));
    }

    [Fact]
    public async Task Delete_OneUsersLink_LeavesAnotherUsersLinkToTheSharedFeature()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var aliceVehicle = await CreateVehicleAsync(alice);
        var bobVehicle = await CreateVehicleAsync(bob);
        var aliceTag = (await (await AddTagAsync(alice, aliceVehicle.Id, "Sunroof")).Content.ReadFromJsonAsync<VehicleTagDto>())!;
        await AddTagAsync(bob, bobVehicle.Id, "sunroof");

        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/api/vehicles/{aliceVehicle.Id}/tags/{aliceTag.Id}")).StatusCode);

        Assert.Empty((await alice.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{aliceVehicle.Id}"))!.Tags);
        Assert.Equal(["Sunroof"], (await bob.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{bobVehicle.Id}"))!.Tags.Select(t => t.Name));
    }

    [Fact]
    public async Task Delete_UnknownTagOrAnotherUsersTag_Returns404()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        var tag = (await (await AddTagAsync(alice, vehicle.Id, "mine")).Content.ReadFromJsonAsync<VehicleTagDto>())!;

        var missing = await alice.DeleteAsync($"/api/vehicles/{vehicle.Id}/tags/{Guid.NewGuid()}");
        var foreign = await factory.CreateClientForUser(Bob).DeleteAsync($"/api/vehicles/{vehicle.Id}/tags/{tag.Id}");

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Single((await alice.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!.Tags);
    }

    [Fact]
    public async Task Endpoints_WithoutToken_Return401()
    {
        var anonymous = factory.CreateClientWithToken(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await AddTagAsync(anonymous, Guid.NewGuid(), "x")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/vehicles/{Guid.NewGuid()}/tags/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task UpdatingTheVehicle_KeepsItsTagsInTheResponse()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await AddTagAsync(client, vehicle.Id, "sunroof");

        var response = await client.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}", new VehicleRequest { Make = "Ford", Model = "Ranger", Year = 2020 });

        var updated = await response.Content.ReadFromJsonAsync<VehicleDetailDto>();
        Assert.Equal(["sunroof"], updated!.Tags.Select(t => t.Name));
    }
}
