using System.Net;
using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

public class VehicleSearchEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    private const string Outback = "2020 Subaru Outback";
    private const string F150 = "2020 Ford F-150";
    private const string Porsche911 = "2020 Porsche 911";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<VehicleDetailDto> CreateAsync(HttpClient client, string make, string model, string? color = null, int? mileage = null, int? doors = null, int year = 2020)
    {
        var response = await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest
        {
            Make = make, Model = model, Color = color, Mileage = mileage, Doors = doors, Year = year,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
    }

    private static async Task<PagedResultDto<VehicleSummaryDto>> ListAsync(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>($"/api/vehicles{query}"))!;

    private async Task<HttpClient> SeedAsync()
    {
        var alice = factory.CreateClientForUser(Alice);
        var blue = await CreateAsync(alice, "Subaru", "Outback", "Blue", 42_000, 4);
        var red = await CreateAsync(alice, "Ford", "F-150", "Red", 120_000, 4);
        var silver = await CreateAsync(alice, "Porsche", "911", "Silver", 5_000, 2);
        await factory.InsertTagAsync(blue.Id, "Tow package");
        await factory.InsertTagAsync(red.Id, "tow package");
        await factory.InsertTagAsync(silver.Id, "Convertible");
        return alice;
    }

    [Theory]
    [InlineData("?q=outback", Outback)]
    [InlineData("?q=FORD", F150)]
    [InlineData("?q=convert", Porsche911)]
    [InlineData("?make=porsche", Porsche911)]
    [InlineData("?color=RED", F150)]
    [InlineData("?doors=2", Porsche911)]
    [InlineData("?tag=Convertible", Porsche911)]
    [InlineData("?minMileage=100000", F150)]
    [InlineData("?maxMileage=5000", Porsche911)]
    [InlineData("?q=2020%20ford%20f-150", F150)]
    [InlineData("?q=ford&tag=tow%20package&minMileage=1000&maxMileage=200000&doors=4&color=red&make=ford", F150)]
    public async Task Criteria_ReturnOnlyMatchingVehiclesWithFilteredTotals(string query, string expectedName)
    {
        var client = await SeedAsync();

        var page = await ListAsync(client, query);

        Assert.Equal([expectedName], page.Items.Select(item => item.Name));
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(1, page.TotalPages);
    }

    [Fact]
    public async Task TagFilter_MatchesMoreThanOneVehicle()
    {
        var client = await SeedAsync();

        var page = await ListAsync(client, "?tag=TOW%20PACKAGE");

        Assert.Equal([F150, Outback], page.Items.Select(item => item.Name));
    }

    [Theory]
    [InlineData("2012 jeep")]
    [InlineData("2012 JEEP WRANGLER")]
    [InlineData("jeep wrangler")]
    [InlineData("12 jeep")]
    public async Task Search_MatchesTheYearMakeModelText(string text)
    {
        var client = await SeedAsync();
        await CreateAsync(client, "Jeep", "Wrangler", year: 2012);
        await CreateAsync(client, "Jeep", "Cherokee", year: 2015);

        var page = await ListAsync(client, $"?q={Uri.EscapeDataString(text)}");

        Assert.Contains("2012 Jeep Wrangler", page.Items.Select(item => item.Name));
        Assert.DoesNotContain("2015 Jeep Cherokee", page.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task Search_ReportsWhichOtherFieldsMatched()
    {
        var client = await SeedAsync();

        var tow = await ListAsync(client, "?q=tow");
        var silver = await ListAsync(client, "?q=silv");

        var outback = Assert.Single(tow.Items, item => item.Name == Outback);
        var feature = Assert.Single(outback.Matches);
        Assert.Equal("Feature", feature.Field);
        Assert.Equal("Tow package", feature.Text);
        var porsche = Assert.Single(silver.Items);
        Assert.Equal([("Color", "Silver")], porsche.Matches.Select(match => (match.Field, match.Text)));
    }

    [Fact]
    public async Task WithoutSearchText_NoMatchesAreReported()
    {
        var client = await SeedAsync();

        var page = await ListAsync(client, "?color=red");

        Assert.All(page.Items, item => Assert.Empty(item.Matches));
    }

    [Fact]
    public async Task Search_YearMakeModelTextDoesNotMatchAnotherUsersVehicle()
    {
        await CreateAsync(factory.CreateClientForUser(Alice), "Jeep", "Wrangler", year: 2012);

        var page = await ListAsync(factory.CreateClientForUser(Bob), "?q=2012%20jeep");

        Assert.Empty(page.Items);
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
            await CreateAsync(client, "Ford", $"Truck {i:D2}");
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
        await CreateAsync(bob, "Subaru", "Forester");

        var page = await ListAsync(bob, "?q=subaru");

        Assert.Equal(["2020 Subaru Forester"], page.Items.Select(item => item.Name));
        Assert.Empty((await ListAsync(bob, "?q=outback")).Items);
    }

    [Fact]
    public async Task FilterOptions_ListDistinctValuesFromTheCallersGarageOnly()
    {
        var alice = await SeedAsync();
        await CreateAsync(alice, "ford", "Mustang", "red");
        var bob = factory.CreateClientForUser(Bob);
        var bobCar = await CreateAsync(bob, "Tesla", "3", "White");
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
