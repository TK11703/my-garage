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

    private static VehicleRequest Request(string make = "Ford", string model = "F-150", int year = 2020) =>
        new() { Make = make, Model = model, Year = year };

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
            Make = " Ford ", Model = "F-150", Trim = "XLT", Color = "Blue", Mileage = 1000, Doors = 4, Year = 2020,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<VehicleDetailDto>();
        Assert.NotNull(created);
        Assert.Equal($"/api/vehicles/{created.Id}", response.Headers.Location?.ToString());
        Assert.Equal("2020 Ford F-150", created.Name);
        Assert.Equal("XLT", created.Trim);
        Assert.Equal(4, created.Doors);
        Assert.Empty(created.Tags);
        Assert.Empty(created.Images);
    }

    [Fact]
    public async Task Create_WithoutNickname_Returns201AndComposedName()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<VehicleDetailDto>();
        Assert.Equal("2020 Ford F-150", created!.Name);
        var item = Assert.Single((await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles"))!.Items);
        Assert.Equal("2020 Ford F-150", item.Name);
    }

    [Fact]
    public async Task Create_ResponseJson_HasNoNameOrNicknameProperty()
    {
        var client = factory.CreateClientForUser(Alice);
        var created = await CreateAsync(client, Request());

        var detailJson = await client.GetStringAsync($"/api/vehicles/{created.Id}");
        var listJson = await client.GetStringAsync("/api/vehicles");

        foreach (var json in new[] { detailJson, listJson })
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            Assert.False(ContainsProperty(document.RootElement, "name"));
            Assert.False(ContainsProperty(document.RootElement, "nickname"));
        }
    }

    private static bool ContainsProperty(System.Text.Json.JsonElement element, string name) =>
        element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Object => element.EnumerateObject()
                .Any(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) || ContainsProperty(property.Value, name)),
            System.Text.Json.JsonValueKind.Array => element.EnumerateArray().Any(item => ContainsProperty(item, name)),
            _ => false,
        };

    [Fact]
    public async Task List_OrdersByYearNewestFirstThenMakeThenModel_WithUnknownYearLast()
    {
        var client = factory.CreateClientForUser(Alice);
        await CreateAsync(client, Request("Toyota", "Tacoma", 2018));
        await CreateAsync(client, Request("Ford", "Ranger", 2019));
        await CreateAsync(client, Request("Ford", "F-150", 2021));
        await CreateAsync(client, Request("Ford", "F-150", 2015));
        await factory.InsertVehicleWithoutYearAsync(Alice, "Ford", "F-150");
        await CreateAsync(client, Request("ford", "Bronco", 2022));
        await CreateAsync(client, Request("Audi", "A4", 2019));

        var page = await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles");

        Assert.Equal(
            ["2022 ford Bronco", "2021 Ford F-150", "2019 Audi A4", "2019 Ford Ranger", "2018 Toyota Tacoma", "2015 Ford F-150", "Ford F-150"],
            page!.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task Create_InvalidRequest_Returns400ValidationProblem()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Make = "", Model = "F-150", Mileage = -5, Doors = 9 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors.Keys, key => key.Contains("Make", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(problem.Errors.Keys, key => key.Contains("Mileage", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(problem.Errors.Keys, key => key.Contains("Doors", StringComparison.OrdinalIgnoreCase));
        Assert.Empty((await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles"))!.Items);
    }

    [Fact]
    public async Task Create_IgnoresOwnerIdentifiersInTheBody()
    {
        var alice = factory.CreateClientForUser(Alice);
        var json = new StringContent(
            """{"make":"Ford","model":"F-150","year":2020,"ownerId":"bob-oid","userId":"bob-oid"}""",
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
            await CreateAsync(client, Request($"Make{i:D2}"));
        }

        var page = await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles");

        Assert.NotNull(page);
        Assert.Equal(12, page.Items.Count);
        Assert.Equal(1, page.Page);
        Assert.Equal(12, page.PageSize);
        Assert.Equal(13, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal("2020 Make01 F-150", page.Items[0].Name);
    }

    [Fact]
    public async Task List_PageBeyondLast_ReturnsLastPageWithItsRealNumber()
    {
        var client = factory.CreateClientForUser(Alice);
        for (var i = 1; i <= 13; i++)
        {
            await CreateAsync(client, Request($"Make{i:D2}"));
        }

        var page = await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles?page=999");

        Assert.NotNull(page);
        Assert.Equal(2, page.Page);
        Assert.Single(page.Items);
        Assert.Equal("2020 Make13 F-150", page.Items[0].Name);
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
        var created = await CreateAsync(client, Request());

        var vehicle = await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{created.Id}");

        Assert.NotNull(vehicle);
        Assert.Equal(created.Id, vehicle.Id);
        Assert.Equal("2020 Ford F-150", vehicle.Name);
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
        var created = await CreateAsync(alice, Request("Subaru", "Outback"));

        var bobList = await bob.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles");
        var bobGet = await bob.GetAsync($"/api/vehicles/{created.Id}");

        Assert.NotNull(bobList);
        Assert.Empty(bobList.Items);
        Assert.Equal(HttpStatusCode.NotFound, bobGet.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync($"/api/vehicles/{created.Id}")).StatusCode);
    }
}
