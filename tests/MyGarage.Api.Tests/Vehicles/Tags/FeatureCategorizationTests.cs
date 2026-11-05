using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Api.Tests.Common;
using MyGarage.Api.Vehicles.Tags;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles.Tags;

public class FeatureCategorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync()
    {
        factory.FeatureCategorizer.Reset();
        return factory.ResetDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<VehicleDetailDto> CreateVehicleAsync(HttpClient client, IReadOnlyList<StickerFeatureRequest>? features = null)
    {
        var response = await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020, StickerFeatures = features });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
    }

    private static Task<HttpResponseMessage> AddTagAsync(HttpClient client, Guid vehicleId, string name) =>
        client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/tags", new AddVehicleTagRequest { Name = name });

    private static async Task<IReadOnlyList<VehicleTagDto>> TagsAsync(HttpClient client, Guid vehicleId) =>
        (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicleId}"))!.Tags;

    private static string? ByName(string name) => name.Contains("airbag", StringComparison.OrdinalIgnoreCase) ? "Safety" : "Interior";

    [Fact]
    public async Task AddingAFeature_IsCategorizedBeforeTheAnswer()
    {
        factory.FeatureCategorizer.Answer = ByName;
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await AddTagAsync(client, vehicle.Id, "Side Airbags");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Safety", (await response.Content.ReadFromJsonAsync<VehicleTagDto>())!.Category);
        Assert.Equal(["Safety"], (await TagsAsync(client, vehicle.Id)).Select(tag => tag.Category));
        Assert.Equal(["Side Airbags"], Assert.Single(factory.FeatureCategorizer.Calls));
    }

    [Fact]
    public async Task AFeatureThatAlreadyHasACategory_IsNotSentToTheModelAgain()
    {
        factory.FeatureCategorizer.Answer = ByName;
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        await AddTagAsync(alice, (await CreateVehicleAsync(alice)).Id, "Side Airbags");
        var bobsVehicle = await CreateVehicleAsync(bob);

        var response = await AddTagAsync(bob, bobsVehicle.Id, "side airbags");

        Assert.Equal("Safety", (await response.Content.ReadFromJsonAsync<VehicleTagDto>())!.Category);
        Assert.Single(factory.FeatureCategorizer.Calls);
    }

    [Fact]
    public async Task WhenTheModelFails_TheFeatureIsStillAddedUnderOther_AndTheSweepFillsItInLater()
    {
        factory.FeatureCategorizer.FailureReason = "timeout";
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await AddTagAsync(client, vehicle.Id, "Side Airbags");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Other", (await response.Content.ReadFromJsonAsync<VehicleTagDto>())!.Category);

        factory.FeatureCategorizer.FailureReason = null;
        factory.FeatureCategorizer.Answer = ByName;
        var done = await factory.Services.GetRequiredService<FeatureCategorizationService>().CategorizePendingAsync(CancellationToken.None);

        Assert.Equal(1, done);
        Assert.Equal(["Safety"], (await TagsAsync(client, vehicle.Id)).Select(tag => tag.Category));
    }

    [Fact]
    public async Task WhenCategorizingIsOff_TheFeatureIsAddedUnderOtherWithoutACall()
    {
        factory.FeatureCategorizer.IsEnabled = false;
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await AddTagAsync(client, vehicle.Id, "Side Airbags");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Other", (await response.Content.ReadFromJsonAsync<VehicleTagDto>())!.Category);
        Assert.Empty(factory.FeatureCategorizer.Calls);
    }

    [Fact]
    public async Task ACategoryTheModelDoesNotAnswer_StaysPendingForTheNextTry()
    {
        factory.FeatureCategorizer.Answer = name => name == "Side Airbags" ? "Safety" : null;
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        await AddTagAsync(client, vehicle.Id, "Side Airbags");
        var unanswered = await AddTagAsync(client, vehicle.Id, "Mystery Item");
        factory.FeatureCategorizer.Answer = _ => "Packages";
        var done = await factory.Services.GetRequiredService<FeatureCategorizationService>().CategorizePendingAsync(CancellationToken.None);

        Assert.Equal("Other", (await unanswered.Content.ReadFromJsonAsync<VehicleTagDto>())!.Category);
        Assert.Equal(1, done);
        Assert.Equal(["Safety", "Packages"], (await TagsAsync(client, vehicle.Id)).Select(tag => tag.Category));
    }

    [Fact]
    public async Task SavingAVehicleWithStickerFeatures_CategorizesTheNewOnesInOneCall()
    {
        factory.FeatureCategorizer.Answer = ByName;
        var client = factory.CreateClientForUser(Alice);

        var vehicle = await CreateVehicleAsync(client, [new StickerFeatureRequest { Name = "Curtain Airbags" }, new StickerFeatureRequest { Name = "Leather Seats" }]);

        Assert.Equal(
            [("Curtain Airbags", "Safety"), ("Leather Seats", "Interior")],
            vehicle.Tags.OrderBy(tag => tag.Name).Select(tag => (tag.Name, tag.Category)));
        Assert.Single(factory.FeatureCategorizer.Calls);
    }

    [Fact]
    public async Task ASweepWithNothingPending_MakesNoCall()
    {
        var done = await factory.Services.GetRequiredService<FeatureCategorizationService>().CategorizePendingAsync(CancellationToken.None);

        Assert.Equal(0, done);
        Assert.Empty(factory.FeatureCategorizer.Calls);
    }

    [Fact]
    public async Task TheSweepSendsThePendingNamesOldestFirstInOneCall()
    {
        factory.FeatureCategorizer.FailureReason = "service_error";
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await AddTagAsync(client, vehicle.Id, "First");
        await AddTagAsync(client, vehicle.Id, "Second");
        factory.FeatureCategorizer.Reset();

        await factory.Services.GetRequiredService<FeatureCategorizationService>().CategorizePendingAsync(CancellationToken.None);

        Assert.Equal(["First", "Second"], Assert.Single(factory.FeatureCategorizer.Calls));
    }
}
