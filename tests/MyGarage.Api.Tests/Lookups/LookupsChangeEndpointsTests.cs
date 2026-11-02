using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Lookups;

namespace MyGarage.Api.Tests.Lookups;

public class LookupsChangeEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<LookupsDto> GetAsync(HttpClient client, bool includeHidden = false) =>
        (await client.GetFromJsonAsync<LookupsDto>($"/api/lookups?includeHidden={includeHidden.ToString().ToLowerInvariant()}"))!;

    private static Task<HttpResponseMessage> AddAsync(HttpClient client, string kind, string? name) =>
        client.PostAsJsonAsync($"/api/lookups/{kind}", new AddLookupItemRequest { Name = name });

    private static Task<HttpResponseMessage> SetHiddenAsync(HttpClient client, string kind, Guid id, bool isHidden) =>
        client.PutAsJsonAsync($"/api/lookups/{kind}/{id}/visibility", new SetLookupVisibilityRequest { IsHidden = isHidden });

    [Fact]
    public async Task Post_ValidName_Returns201WithTheNewShownItemListedAfterTheDefaults()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await AddAsync(client, LookupKinds.FuelTypes, "  Plug-in Hybrid ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = (await response.Content.ReadFromJsonAsync<LookupItemDto>())!;
        Assert.Equal("Plug-in Hybrid", item.Name);
        Assert.False(item.IsDefault);
        Assert.False(item.IsHidden);
        var fuel = (await GetAsync(client)).FuelTypes;
        Assert.Equal(["Gasoline", "Diesel", "Electric", "Hybrid", "Plug-in Hybrid"], fuel.Select(i => i.Name));
        Assert.Equal(item.Id, fuel[^1].Id);
    }

    [Fact]
    public async Task Post_CustomItemStaysPrivate()
    {
        await AddAsync(factory.CreateClientForUser(Alice), LookupKinds.Drivetrains, "Rear-mid");

        var bob = await GetAsync(factory.CreateClientForUser(Bob), includeHidden: true);

        Assert.DoesNotContain(bob.Drivetrains, item => item.Name == "Rear-mid");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234567890123456789012345678901")]
    public async Task Post_InvalidName_Returns400ValidationProblem(string name)
    {
        var response = await AddAsync(factory.CreateClientForUser(Alice), LookupKinds.FuelTypes, name);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        Assert.Contains(problem.Errors.Keys, key => key.Equals("Name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Post_UnknownKind_Returns404Problem()
    {
        var response = await AddAsync(factory.CreateClientForUser(Alice), "body-styles", "Sedan");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Post_DuplicateOfADefault_Returns409AndSaysSo()
    {
        var response = await AddAsync(factory.CreateClientForUser(Alice), LookupKinds.FuelTypes, "diesel");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>())!;
        Assert.Contains("already exists", problem.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hidden", problem.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_DuplicateOfAHiddenItem_Returns409MentioningThatItIsHidden()
    {
        var alice = factory.CreateClientForUser(Alice);
        var sedan = (await GetAsync(alice)).VehicleTypes.Single(item => item.Name == "Sedan");
        await SetHiddenAsync(alice, LookupKinds.VehicleTypes, sedan.Id, true);

        var response = await AddAsync(alice, LookupKinds.VehicleTypes, "Sedan");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("hidden", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_FiftyFirstCustomItem_Returns409LimitReached()
    {
        var alice = factory.CreateClientForUser(Alice);
        for (var i = 1; i <= 50; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await AddAsync(alice, LookupKinds.Transmissions, $"Gearbox {i}")).StatusCode);
        }

        var response = await AddAsync(alice, LookupKinds.Transmissions, "Gearbox 51");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("50", (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
    }

    [Fact]
    public async Task Put_HideThenShow_ChangesOnlyTheCallersLists_AndRepeatsAreIdempotent()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var sedan = (await GetAsync(alice)).VehicleTypes.Single(item => item.Name == "Sedan");

        var hidden = await SetHiddenAsync(alice, LookupKinds.VehicleTypes, sedan.Id, true);
        var hiddenAgain = await SetHiddenAsync(alice, LookupKinds.VehicleTypes, sedan.Id, true);

        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        Assert.True((await hidden.Content.ReadFromJsonAsync<LookupItemDto>())!.IsHidden);
        Assert.Equal(HttpStatusCode.OK, hiddenAgain.StatusCode);
        Assert.DoesNotContain((await GetAsync(alice)).VehicleTypes, item => item.Id == sedan.Id);
        Assert.Contains((await GetAsync(alice, includeHidden: true)).VehicleTypes, item => item.Id == sedan.Id && item.IsHidden);
        Assert.Contains((await GetAsync(bob)).VehicleTypes, item => item.Id == sedan.Id && !item.IsHidden);

        var shown = await SetHiddenAsync(alice, LookupKinds.VehicleTypes, sedan.Id, false);

        Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
        Assert.False((await shown.Content.ReadFromJsonAsync<LookupItemDto>())!.IsHidden);
        Assert.Contains((await GetAsync(alice)).VehicleTypes, item => item.Id == sedan.Id);
    }

    [Fact]
    public async Task Put_MissingIsHidden_Returns400()
    {
        var alice = factory.CreateClientForUser(Alice);
        var sedan = (await GetAsync(alice)).VehicleTypes.First();

        var response = await alice.PutAsync($"/api/lookups/{LookupKinds.VehicleTypes}/{sedan.Id}/visibility", JsonContent.Create(new { }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_UnknownItemWrongKindOrAnotherUsersItem_Returns404AndChangesNothing()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var sedan = (await GetAsync(alice)).VehicleTypes.Single(item => item.Name == "Sedan");
        var bobsItem = (await (await AddAsync(bob, LookupKinds.FuelTypes, "Bob's special")).Content.ReadFromJsonAsync<LookupItemDto>())!;

        Assert.Equal(HttpStatusCode.NotFound, (await SetHiddenAsync(alice, LookupKinds.VehicleTypes, Guid.NewGuid(), true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetHiddenAsync(alice, LookupKinds.FuelTypes, sedan.Id, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetHiddenAsync(alice, LookupKinds.FuelTypes, bobsItem.Id, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetHiddenAsync(alice, "body-styles", sedan.Id, true)).StatusCode);

        Assert.DoesNotContain((await GetAsync(alice, includeHidden: true)).VehicleTypes, item => item.IsHidden);
        Assert.Contains((await GetAsync(bob)).FuelTypes, item => item.Id == bobsItem.Id && !item.IsHidden);
    }

    [Fact]
    public async Task ChangeEndpoints_WithoutToken_Return401()
    {
        var anonymous = factory.CreateClientWithToken(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await AddAsync(anonymous, LookupKinds.FuelTypes, "X")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SetHiddenAsync(anonymous, LookupKinds.FuelTypes, Guid.NewGuid(), true)).StatusCode);
    }
}
