using System.Net;
using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Lookups;

namespace MyGarage.Api.Tests.Lookups;

public class LookupsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<LookupsDto> GetAsync(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<LookupsDto>($"/api/lookups{query}"))!;

    [Fact]
    public async Task Get_NewUser_ReturnsDefaultsInOrderAllShown()
    {
        var lookups = await GetAsync(factory.CreateClientForUser(Alice));

        Assert.Equal(
            ["Sedan", "SUV", "Coupe", "Hatchback", "Convertible", "Truck", "Van", "Wagon", "Minivan", "Crossover", "Motorcycle"],
            lookups.VehicleTypes.Select(item => item.Name));
        Assert.Equal(["Gasoline", "Diesel", "Electric", "Hybrid"], lookups.FuelTypes.Select(item => item.Name));
        Assert.Equal(["Automatic", "Manual"], lookups.Transmissions.Select(item => item.Name));
        Assert.Equal(["FWD", "RWD", "AWD", "4WD"], lookups.Drivetrains.Select(item => item.Name));
        Assert.All(
            lookups.VehicleTypes.Concat(lookups.FuelTypes).Concat(lookups.Transmissions).Concat(lookups.Drivetrains),
            item =>
            {
                Assert.True(item.IsDefault);
                Assert.False(item.IsHidden);
                Assert.NotEqual(Guid.Empty, item.Id);
            });
    }

    [Fact]
    public async Task Get_HiddenItem_IsOmittedByDefaultAndReturnedWhenRequested()
    {
        var alice = factory.CreateClientForUser(Alice);
        var sedan = (await GetAsync(alice)).VehicleTypes.Single(item => item.Name == "Sedan");
        await factory.HideLookupItemAsync(Alice, sedan.Id);

        var shown = await GetAsync(alice);
        var all = await GetAsync(alice, "?includeHidden=true");

        Assert.DoesNotContain(shown.VehicleTypes, item => item.Id == sedan.Id);
        Assert.Contains(all.VehicleTypes, item => item.Id == sedan.Id && item.IsHidden);
    }

    [Fact]
    public async Task Get_CustomItems_FollowDefaultsAndNeverReachAnotherUser()
    {
        await factory.InsertLookupItemAsync(Alice, "drivetrain", "Rear-mid");
        var hiddenId = await factory.InsertLookupItemAsync(Alice, "drivetrain", "Front-mid");
        await factory.HideLookupItemAsync(Alice, hiddenId);

        var alice = await GetAsync(factory.CreateClientForUser(Alice), "?includeHidden=true");
        var bob = await GetAsync(factory.CreateClientForUser(Bob), "?includeHidden=true");

        Assert.Equal(["FWD", "RWD", "AWD", "4WD", "Front-mid", "Rear-mid"], alice.Drivetrains.Select(item => item.Name));
        Assert.Contains(alice.Drivetrains, item => item.Name == "Rear-mid" && !item.IsDefault);
        Assert.Equal(["FWD", "RWD", "AWD", "4WD"], bob.Drivetrains.Select(item => item.Name));
    }

    [Fact]
    public async Task Get_IgnoresOwnerIdentifiersInTheRequest()
    {
        await factory.InsertLookupItemAsync(Alice, "fuel_type", "Biodiesel");
        var bob = factory.CreateClientForUser(Bob);

        var lookups = await GetAsync(bob, "?ownerId=alice-oid&userId=alice-oid");

        Assert.DoesNotContain(lookups.FuelTypes, item => item.Name == "Biodiesel");
    }

    [Fact]
    public async Task Get_WithoutToken_Returns401Problem()
    {
        var response = await factory.CreateClientWithToken(null).GetAsync("/api/lookups");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
