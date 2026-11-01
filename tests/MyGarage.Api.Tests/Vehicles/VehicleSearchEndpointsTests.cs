using System.Net;
using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

public class VehicleSearchEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<VehicleDetailDto> CreateAsync(HttpClient client, string nickname, string make, string model, string? color = null, int? mileage = null, int? doors = null)
    {
        var response = await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest
        {
            Nickname = nickname, Make = make, Model = model, Color = color, Mileage = mileage, Doors = doors,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
    }

    private static async Task<PagedResultDto<VehicleSummaryDto>> ListAsync(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>($"/api/vehicles{query}"))!;

    private async Task<HttpClient> SeedAsync()
    {
        var alice = factory.CreateClientForUser(Alice);
        var blue = await CreateAsync(alice, "Blue Streak", "Subaru", "Outback", "Blue", 42_000, 4);
        var red = await CreateAsync(alice, "Red Rocket", "Ford", "F-150", "Red", 120_000, 4);
        var silver = await CreateAsync(alice, "Garage Queen", "Porsche", "911", "Silver", 5_000, 2);
        await factory.InsertTagAsync(blue.Id, "Tow package");
        await factory.InsertTagAsync(red.Id, "tow package");
        await factory.InsertTagAsync(silver.Id, "Convertible");
        return alice;
    }

    [Theory]
    [InlineData("?q=streak", "Blue Streak")]
    [InlineData("?q=FORD", "Red Rocket")]
    [InlineData("?q=convert", "Garage Queen")]
    [InlineData("?make=porsche", "Garage Queen")]
    [InlineData("?color=RED", "Red Rocket")]
    [InlineData("?doors=2", "Garage Queen")]
    [InlineData("?tag=Convertible", "Garage Queen")]
    [InlineData("?minMileage=100000", "Red Rocket")]
    [InlineData("?maxMileage=5000", "Garage Queen")]
    [InlineData("?q=ford&tag=tow%20package&minMileage=1000&maxMileage=200000&doors=4&color=red&make=ford", "Red Rocket")]
    public async Task Criteria_ReturnOnlyMatchingVehiclesWithFilteredTotals(string query, string expectedNickname)
    {
        var client = await SeedAsync();

        var page = await ListAsync(client, query);

        Assert.Equal([expectedNickname], page.Items.Select(item => item.Nickname));
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, page.TotalPages);
    }

    [Fact]
    public async Task TagFilter_MatchesMoreThanOneVehicle()
    {
        var client = await SeedAsync();

        var page = await ListAsync(client, "?tag=TOW%20PACKAGE");

        Assert.Equal(["Blue Streak", "Red Rocket"], page.Items.Select(item => item.Nickname));
    }

    [Fact]
    public async Task NoMatches_ReturnsAnEmptyPageOne()
    {
        var client = await SeedAsync();

        var page = await ListAsync(client, "?q=zzz&page=3");

        Assert.Empty(page.Items);
        Assert.Equal(1, page.Page);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task NarrowingResults_ClampsToTheLastMatchingPage()
    {
        var client = factory.CreateClientForUser(Alice);
        for (var i = 1; i <= 14; i++)
        {
            await CreateAsync(client, $"Truck {i:D2}", "Ford", "F-150");
        }

        var page = await ListAsync(client, "?make=ford&page=9");

        Assert.Equal(2, page.Page);
        Assert.Equal(14, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
    }

    [Theory]
    [InlineData("?minMileage=5000&maxMileage=1000")]
    [InlineData("?doors=9")]
    [InlineData("?minMileage=-1")]
    [InlineData("?doors=abc")]
    public async Task InvalidCriteria_Return400ValidationProblem(string query)
    {
        var response = await factory.CreateClientForUser(Alice).GetAsync($"/api/vehicles{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task QueryOver100Characters_Returns400()
    {
        var response = await factory.CreateClientForUser(Alice).GetAsync($"/api/vehicles?q={new string('a', 101)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OtherUsersVehicles_NeverAppearInSearchResults()
    {
        await SeedAsync();
        var bob = factory.CreateClientForUser(Bob);
        await CreateAsync(bob, "Bob's Subaru", "Subaru", "Forester");

        var page = await ListAsync(bob, "?q=subaru");

        Assert.Equal(["Bob's Subaru"], page.Items.Select(item => item.Nickname));
        Assert.Empty((await ListAsync(bob, "?q=streak")).Items);
    }

    [Fact]
    public async Task FilterOptions_ListDistinctValuesFromTheCallersGarageOnly()
    {
        var alice = await SeedAsync();
        await CreateAsync(alice, "Another Ford", "ford", "Mustang", "red");
        var bob = factory.CreateClientForUser(Bob);
        var bobCar = await CreateAsync(bob, "Bob's", "Tesla", "3", "White");
        await factory.InsertTagAsync(bobCar.Id, "autopilot");

        var options = await alice.GetFromJsonAsync<VehicleFilterOptionsDto>("/api/vehicles/filter-options");

        Assert.NotNull(options);
        Assert.Equal(["ford", "porsche", "subaru"], options.Makes.Select(make => make.ToLowerInvariant()));
        Assert.Equal(["blue", "red", "silver"], options.Colors.Select(color => color.ToLowerInvariant()));
        Assert.Equal(["convertible", "tow package"], options.Tags.Select(tag => tag.ToLowerInvariant()));

        var bobOptions = await bob.GetFromJsonAsync<VehicleFilterOptionsDto>("/api/vehicles/filter-options");
        Assert.NotNull(bobOptions);
        Assert.Equal(["Tesla"], bobOptions.Makes);
        Assert.Equal(["autopilot"], bobOptions.Tags);
    }

    [Fact]
    public async Task FilterOptions_EmptyGarage_ReturnsEmptyLists()
    {
        var options = await factory.CreateClientForUser("nobody").GetFromJsonAsync<VehicleFilterOptionsDto>("/api/vehicles/filter-options");

        Assert.NotNull(options);
        Assert.Empty(options.Makes);
        Assert.Empty(options.Colors);
        Assert.Empty(options.Tags);
    }

    [Fact]
    public async Task FilterOptions_WithoutToken_Returns401()
    {
        var response = await factory.CreateClientWithToken(null).GetAsync("/api/vehicles/filter-options");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
