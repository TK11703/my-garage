using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

public class VehiclesEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static VehicleRequest Request(string nickname = "Daily", string make = "Ford", string model = "F-150") =>
        new() { Nickname = nickname, Make = make, Model = model };

    private static async Task<VehicleDetailDto> CreateAsync(HttpClient client, VehicleRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/vehicles", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
    }

    [Fact]
    public async Task Create_ValidRequest_Returns201WithLocationAndDetail()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest
        {
            Nickname = " Daily ", Make = "Ford", Model = "F-150", Trim = "XLT", Color = "Blue", Mileage = 1000, Doors = 4,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<VehicleDetailDto>();
        Assert.NotNull(created);
        Assert.Equal($"/api/vehicles/{created.Id}", response.Headers.Location?.ToString());
        Assert.Equal("Daily", created.Nickname);
        Assert.Equal("XLT", created.Trim);
        Assert.Equal(4, created.Doors);
        Assert.Empty(created.Tags);
        Assert.Empty(created.Images);
    }

    [Fact]
    public async Task Create_InvalidRequest_Returns400ValidationProblem()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Nickname = "", Make = "Ford", Model = "F-150", Mileage = -5, Doors = 9 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors.Keys, key => key.Contains("Nickname", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(problem.Errors.Keys, key => key.Contains("Mileage", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(problem.Errors.Keys, key => key.Contains("Doors", StringComparison.OrdinalIgnoreCase));
        Assert.Empty((await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles"))!.Items);
    }

    [Fact]
    public async Task Create_IgnoresOwnerIdentifiersInTheBody()
    {
        var alice = factory.CreateClientForUser(Alice);
        var json = new StringContent(
            """{"nickname":"Daily","make":"Ford","model":"F-150","ownerId":"bob-oid","userId":"bob-oid"}""",
            Encoding.UTF8,
            "application/json");

        var response = await alice.PostAsync("/api/vehicles", json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Single((await alice.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles"))!.Items);
        Assert.Empty((await factory.CreateClientForUser(Bob).GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles"))!.Items);
    }

    [Fact]
    public async Task List_FirstPage_Returns12CardsWithTotals()
    {
        var client = factory.CreateClientForUser(Alice);
        for (var i = 1; i <= 13; i++)
        {
            await CreateAsync(client, Request($"Car {i:D2}"));
        }

        var page = await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles");

        Assert.NotNull(page);
        Assert.Equal(12, page.Items.Count);
        Assert.Equal(1, page.Page);
        Assert.Equal(12, page.PageSize);
        Assert.Equal(13, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal("Car 01", page.Items[0].Nickname);
    }

    [Fact]
    public async Task List_PageBeyondLast_ReturnsLastPageWithItsRealNumber()
    {
        var client = factory.CreateClientForUser(Alice);
        for (var i = 1; i <= 13; i++)
        {
            await CreateAsync(client, Request($"Car {i:D2}"));
        }

        var page = await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles?page=999");

        Assert.NotNull(page);
        Assert.Equal(2, page.Page);
        Assert.Single(page.Items);
        Assert.Equal("Car 13", page.Items[0].Nickname);
    }

    [Fact]
    public async Task List_EmptyGarage_ReturnsPageOneWithNoItems()
    {
        var page = await factory.CreateClientForUser(Alice).GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles?page=4");

        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Equal(1, page.Page);
        Assert.Equal(0, page.TotalCount);
    }

    [Theory]
    [InlineData("/api/vehicles?page=0")]
    [InlineData("/api/vehicles?pageSize=51")]
    [InlineData("/api/vehicles?pageSize=0")]
    public async Task List_InvalidPaging_Returns400(string url)
    {
        var response = await factory.CreateClientForUser(Alice).GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_ExistingVehicle_Returns200WithDetail()
    {
        var client = factory.CreateClientForUser(Alice);
        var created = await CreateAsync(client, Request("Daily"));

        var vehicle = await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{created.Id}");

        Assert.NotNull(vehicle);
        Assert.Equal(created.Id, vehicle.Id);
        Assert.Equal("Daily", vehicle.Nickname);
    }

    [Fact]
    public async Task Get_UnknownVehicle_Returns404ProblemDetails()
    {
        var response = await factory.CreateClientForUser(Alice).GetAsync($"/api/vehicles/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task EveryEndpoint_WithoutToken_Returns401()
    {
        var anonymous = factory.CreateClientWithToken(null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/vehicles")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/vehicles", Request())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/vehicles/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task OtherUser_SeesNoVehiclesAndCannotFetchAnothersVehicle()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var created = await CreateAsync(alice, Request("Alice's car"));

        var bobList = await bob.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles");
        var bobGet = await bob.GetAsync($"/api/vehicles/{created.Id}");

        Assert.NotNull(bobList);
        Assert.Empty(bobList.Items);
        Assert.Equal(HttpStatusCode.NotFound, bobGet.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/vehicles/{created.Id}")).StatusCode);
    }
}
