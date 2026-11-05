using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles.StickerImport;

public class StickerFeatureSaveTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static VehicleRequest Request(IReadOnlyList<StickerFeatureRequest>? features, string make = "Ford") =>
        new() { Make = make, Model = "F-150", Year = 2020, StickerFeatures = features };

    private static StickerFeatureRequest Feature(string name, params Guid[] replaces) => new() { Name = name, ReplacesTagIds = replaces };

    private static async Task<VehicleDetailDto> CreateAsync(HttpClient client, VehicleRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/vehicles", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
    }

    private static async Task<VehicleDetailDto> GetAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{id}"))!;

    private static async Task AddTagAsync(HttpClient client, Guid vehicleId, string name) =>
        (await client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/tags", new AddVehicleTagRequest { Name = name })).EnsureSuccessStatusCode();

    [Fact]
    public async Task Create_WithStickerFeatures_ReturnsThemOnTheDetail()
    {
        var client = factory.CreateClientForUser(Alice);

        var created = await CreateAsync(client, Request([Feature("Heated Seats"), Feature("Panoramic Sunroof")]));

        Assert.Equal(["Heated Seats", "Panoramic Sunroof"], created.Tags.Select(tag => tag.Name).Order());
        Assert.Equal(2, (await GetAsync(client, created.Id)).Tags.Count);
    }

    [Fact]
    public async Task Update_ReplacesTheListedFeaturesAndAddsTheNewOnesInOneSave()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateAsync(client, Request(null));
        await AddTagAsync(client, vehicle.Id, "Sunroof");
        await AddTagAsync(client, vehicle.Id, "Towing Package");
        var sunroof = (await GetAsync(client, vehicle.Id)).Tags.Single(tag => tag.Name == "Sunroof");

        var response = await client.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}", Request([Feature("Panoramic Sunroof", sunroof.Id)], make: "Chevrolet"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
        Assert.Equal("Chevrolet", updated.Make);
        Assert.Equal(["Panoramic Sunroof", "Towing Package"], updated.Tags.Select(tag => tag.Name).Order());
    }

    [Fact]
    public async Task Update_AResultAboveTheLimit_Returns400NamingStickerFeaturesAndChangesNothing()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateAsync(client, Request(null));
        var existing = MyGarage.Database.Tags.VehicleTagRepository.MaxTagsPerVehicle - 1;
        for (var i = 1; i <= existing; i++)
        {
            await AddTagAsync(client, vehicle.Id, $"tag {i}");
        }

        var response = await client.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}", Request([Feature("one"), Feature("two")], make: "Changed"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        Assert.Contains("StickerFeatures", problem.Errors.Keys);
        var stored = await GetAsync(client, vehicle.Id);
        Assert.Equal("Ford", stored.Make);
        Assert.Equal(existing, stored.Tags.Count);
    }

    [Fact]
    public async Task Create_WithMoreThanTheLimitOfFeatures_Returns400()
    {
        var client = factory.CreateClientForUser(Alice);
        var tooMany = MyGarage.Database.Tags.VehicleTagRepository.MaxTagsPerVehicle + 1;

        var response = await client.PostAsJsonAsync("/api/vehicles", Request([.. Enumerable.Range(1, tooMany).Select(i => Feature($"feature {i}"))]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0L, await factory.ExecuteScalarAsync("SELECT count(*) FROM vehicles"));
    }

    [Fact]
    public async Task Create_WithReplacedTagIds_Returns400()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsJsonAsync("/api/vehicles", Request([Feature("Sunroof", Guid.NewGuid())]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        Assert.Contains("StickerFeatures", problem.Errors.Keys);
        Assert.Equal(0L, await factory.ExecuteScalarAsync("SELECT count(*) FROM vehicles"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Update_WithoutStickerFeatures_LeavesTheFeaturesUntouched(bool emptyList)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateAsync(client, Request(null));
        await AddTagAsync(client, vehicle.Id, "Sunroof");

        var response = await client.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}", Request(emptyList ? [] : null, make: "Chevrolet"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Sunroof"], (await GetAsync(client, vehicle.Id)).Tags.Select(tag => tag.Name));
    }

    [Fact]
    public async Task AnotherUsersVehicle_Is404_AndTheirTagsAreNeverTouched()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var theirs = await CreateAsync(alice, Request(null));
        await AddTagAsync(alice, theirs.Id, "Sunroof");
        var theirTag = (await GetAsync(alice, theirs.Id)).Tags.Single();

        var response = await bob.PutAsJsonAsync($"/api/vehicles/{theirs.Id}", Request([Feature("Panoramic Sunroof", theirTag.Id)], make: "Hacked"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var stored = await GetAsync(alice, theirs.Id);
        Assert.Equal("Ford", stored.Make);
        Assert.Equal(["Sunroof"], stored.Tags.Select(tag => tag.Name));
    }

    [Fact]
    public async Task ReplacingTheirTagIdsOnMyOwnVehicle_DeletesNothingOfTheirs()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var theirs = await CreateAsync(alice, Request(null));
        await AddTagAsync(alice, theirs.Id, "Sunroof");
        var theirTag = (await GetAsync(alice, theirs.Id)).Tags.Single();
        var mine = await CreateAsync(bob, Request(null));

        var response = await bob.PutAsJsonAsync($"/api/vehicles/{mine.Id}", Request([Feature("Panoramic Sunroof", theirTag.Id)]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Sunroof"], (await GetAsync(alice, theirs.Id)).Tags.Select(tag => tag.Name));
        Assert.Equal(["Panoramic Sunroof"], (await GetAsync(bob, mine.Id)).Tags.Select(tag => tag.Name));
    }
}
