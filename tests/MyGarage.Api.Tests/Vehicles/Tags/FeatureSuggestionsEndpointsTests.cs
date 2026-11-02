using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles.Tags;

public class FeatureSuggestionsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
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

    private static async Task<VehicleTagDto> AddFeatureAsync(HttpClient client, Guid vehicleId, string name)
    {
        var response = await client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/tags", new AddVehicleTagRequest { Name = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<VehicleTagDto>())!;
    }

    private static async Task<string[]> SuggestAsync(HttpClient client, string? query)
    {
        var url = query is null ? "/api/features" : $"/api/features?query={Uri.EscapeDataString(query)}";
        return (await client.GetFromJsonAsync<string[]>(url))!;
    }

    [Fact]
    public async Task WithoutToken_Returns401()
    {
        var response = await factory.CreateClientWithToken(null).GetAsync("/api/features?query=sun");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankOrMissingQuery_ReturnsAnEmptyArray(string? query)
    {
        var client = factory.CreateClientForUser(Alice);
        await AddFeatureAsync(client, (await CreateVehicleAsync(client)).Id, "Sunroof");

        Assert.Empty(await SuggestAsync(client, query));
    }

    [Fact]
    public async Task QueryLongerThan30Characters_ReturnsAnEmptyArray()
    {
        var client = factory.CreateClientForUser(Alice);
        await AddFeatureAsync(client, (await CreateVehicleAsync(client)).Id, "Sunroof");

        Assert.Empty(await SuggestAsync(client, "sunroof" + new string('x', 24)));
        Assert.Equal(["Sunroof"], await SuggestAsync(client, "sunroof"));
    }

    [Fact]
    public async Task Query_MatchesContainedTextIgnoringCase()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await AddFeatureAsync(client, vehicle.Id, "Panoramic Sunroof");
        await AddFeatureAsync(client, vehicle.Id, "Heated Seats");

        Assert.Equal(["Panoramic Sunroof"], await SuggestAsync(client, "SUNR"));
        Assert.Equal(["Panoramic Sunroof"], await SuggestAsync(client, "  panoramic "));
        Assert.Empty(await SuggestAsync(client, "tow"));
    }

    [Theory]
    [InlineData("%", "100% Grip")]
    [InlineData("_", "front_camera")]
    [InlineData("\\", "a\\b")]
    public async Task WildcardCharacters_AreMatchedLiterally(string query, string expected)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        foreach (var name in new[] { "100% Grip", "front_camera", "a\\b", "Sunroof", "Tow Package" })
        {
            await AddFeatureAsync(client, vehicle.Id, name);
        }

        Assert.Equal([expected], await SuggestAsync(client, query));
    }

    [Fact]
    public async Task OnlyFeaturesLinkedToAVehicle_AreSuggested()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var kept = await AddFeatureAsync(client, vehicle.Id, "Roof rack");
        var removed = await AddFeatureAsync(client, vehicle.Id, "Roof box");
        Assert.Equal(["Roof box", "Roof rack"], await SuggestAsync(client, "roof"));

        await client.DeleteAsync($"/api/vehicles/{vehicle.Id}/tags/{removed.Id}");

        Assert.Equal(["Roof rack"], await SuggestAsync(client, "roof"));
        Assert.Equal(2, await factory.CountFeaturesAsync());
        await client.DeleteAsync($"/api/vehicles/{vehicle.Id}/tags/{kept.Id}");
        Assert.Empty(await SuggestAsync(client, "roof"));
    }

    [Fact]
    public async Task DeletingTheVehicle_RemovesItsFeaturesFromSuggestions()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await AddFeatureAsync(client, vehicle.Id, "Lift kit");

        await client.DeleteAsync($"/api/vehicles/{vehicle.Id}");

        Assert.Empty(await SuggestAsync(client, "lift"));
    }

    [Fact]
    public async Task FeatureUsedOnlyByOneUser_IsSuggestedToAnotherUser()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        await AddFeatureAsync(alice, (await CreateVehicleAsync(alice)).Id, "Winch");

        Assert.Equal(["Winch"], await SuggestAsync(bob, "win"));
    }

    [Fact]
    public async Task Response_IsAPlainArrayOfNamesThatRevealsNoOwnerOrVehicle()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        await AddFeatureAsync(alice, vehicle.Id, "Winch");

        var json = await bob.GetStringAsync("/api/features?query=winch");

        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.All(document.RootElement.EnumerateArray(), item => Assert.Equal(JsonValueKind.String, item.ValueKind));
        Assert.DoesNotContain(Alice, json, StringComparison.Ordinal);
        Assert.DoesNotContain(vehicle.Id.ToString(), json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SameFeatureOnTwoUsersVehicles_IsSuggestedOnceWithTheFirstCreatedCasing()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        await AddFeatureAsync(alice, (await CreateVehicleAsync(alice)).Id, "Heated Seats");
        await AddFeatureAsync(bob, (await CreateVehicleAsync(bob)).Id, "HEATED SEATS");

        Assert.Equal(["Heated Seats"], await SuggestAsync(alice, "heated"));
        Assert.Equal(["Heated Seats"], await SuggestAsync(bob, "heated"));
        Assert.Equal(1, await factory.CountFeaturesAsync());
    }

    [Fact]
    public async Task Suggestions_AreOrderedByNumberOfVehiclesUsingThemThenByName()
    {
        var client = factory.CreateClientForUser(Alice);
        var first = await CreateVehicleAsync(client);
        var second = await CreateVehicleAsync(client);
        var third = await CreateVehicleAsync(client);
        await AddFeatureAsync(client, first.Id, "Alpha pack");
        await AddFeatureAsync(client, first.Id, "Beta pack");
        await AddFeatureAsync(client, second.Id, "Beta pack");
        await AddFeatureAsync(client, third.Id, "Beta pack");
        await AddFeatureAsync(client, first.Id, "Gamma pack");
        await AddFeatureAsync(client, second.Id, "Gamma pack");
        await AddFeatureAsync(client, first.Id, "Delta pack");

        Assert.Equal(["Beta pack", "Gamma pack", "Alpha pack", "Delta pack"], await SuggestAsync(client, "pack"));
    }

    [Fact]
    public async Task Suggestions_AreCappedAt10()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        for (var i = 1; i <= 12; i++)
        {
            await AddFeatureAsync(client, vehicle.Id, $"Option {i:D2}");
        }

        var suggestions = await SuggestAsync(client, "option");

        Assert.Equal(10, suggestions.Length);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => $"Option {i:D2}"), suggestions);
    }
}
