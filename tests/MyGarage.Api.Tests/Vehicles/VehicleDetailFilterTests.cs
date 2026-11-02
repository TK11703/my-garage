using System.Net;
using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Api.Vehicles;
using MyGarage.Shared.Lookups;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

public class VehicleDetailFilterValidatorTests
{
    private readonly VehicleListQueryValidator validator = new();

    private bool IsValid(VehicleListQuery query) => validator.Validate(query).IsValid;

    [Theory]
    [InlineData(1900, true)]
    [InlineData(2200, true)]
    [InlineData(1899, false)]
    [InlineData(2201, false)]
    public void YearBounds_Range(int year, bool valid)
    {
        Assert.Equal(valid, IsValid(new VehicleListQuery(MinYear: year)));
        Assert.Equal(valid, IsValid(new VehicleListQuery(MaxYear: year)));
    }

    [Fact]
    public void MinYear_MustNotExceedMaxYear()
    {
        Assert.True(IsValid(new VehicleListQuery(MinYear: 2000, MaxYear: 2000)));
        Assert.True(IsValid(new VehicleListQuery(MinYear: 1999, MaxYear: 2000)));
        Assert.False(IsValid(new VehicleListQuery(MinYear: 2001, MaxYear: 2000)));
        var errors = validator.Validate(new VehicleListQuery(MinYear: 2001, MaxYear: 2000)).Errors;
        Assert.Contains(errors, error => error.ErrorMessage.Contains("Minimum year", StringComparison.Ordinal));
    }

    [Fact]
    public void LookupIdsAndYearsAreOptional() =>
        Assert.True(IsValid(new VehicleListQuery(VehicleTypeId: Guid.NewGuid(), FuelTypeId: Guid.NewGuid(), TransmissionId: Guid.NewGuid(), DrivetrainId: Guid.NewGuid())));
}

public class VehicleDetailFilterEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Guid IdOf(IEnumerable<LookupItemDto> items, string name) => items.Single(item => item.Name == name).Id;

    private static async Task<PagedResultDto<VehicleSummaryDto>> ListAsync(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>($"/api/vehicles{query}"))!;

    private static async Task CreateAsync(HttpClient client, VehicleRequest request) =>
        (await client.PostAsJsonAsync("/api/vehicles", request)).EnsureSuccessStatusCode();

    private async Task<(HttpClient Client, LookupsDto Lookups)> SeedAsync()
    {
        var client = factory.CreateClientForUser(Alice);
        var lookups = (await client.GetFromJsonAsync<LookupsDto>("/api/lookups"))!;
        await CreateAsync(client, new VehicleRequest
        {
            Nickname = "Tacoma", Make = "Toyota", Model = "Tacoma", Year = 2018, Vin = "1HGCM82633A004352", Engine = "3.5L V6",
            VehicleTypeId = IdOf(lookups.VehicleTypes, "Truck"), FuelTypeId = IdOf(lookups.FuelTypes, "Gasoline"),
            TransmissionId = IdOf(lookups.Transmissions, "Automatic"), DrivetrainId = IdOf(lookups.Drivetrains, "4WD"),
        });
        await CreateAsync(client, new VehicleRequest
        {
            Nickname = "R1T", Make = "Rivian", Model = "R1T", Year = 2022, Engine = "Quad-motor electric",
            VehicleTypeId = IdOf(lookups.VehicleTypes, "Truck"), FuelTypeId = IdOf(lookups.FuelTypes, "Electric"),
            TransmissionId = IdOf(lookups.Transmissions, "Automatic"), DrivetrainId = IdOf(lookups.Drivetrains, "AWD"),
        });
        await CreateAsync(client, new VehicleRequest
        {
            Nickname = "Miata", Make = "Mazda", Model = "MX-5", Year = 1999, Engine = "1.8L I4",
            VehicleTypeId = IdOf(lookups.VehicleTypes, "Convertible"), FuelTypeId = IdOf(lookups.FuelTypes, "Gasoline"),
            TransmissionId = IdOf(lookups.Transmissions, "Manual"), DrivetrainId = IdOf(lookups.Drivetrains, "RWD"),
        });
        return (client, lookups);
    }

    [Fact]
    public async Task Summaries_CarryTheYear()
    {
        var (client, _) = await SeedAsync();

        var page = await ListAsync(client);

        Assert.Equal([1999, 2022, 2018], page.Items.Select(item => item.Year));
        Assert.Equal(["Miata", "R1T", "Tacoma"], page.Items.Select(item => item.Nickname));
    }

    [Theory]
    [InlineData("?q=2018", "Tacoma")]
    [InlineData("?q=317000", "")]
    [InlineData("?q=1hgcm82633", "Tacoma")]
    [InlineData("?q=quad", "R1T")]
    [InlineData("?minYear=2000", "R1T,Tacoma")]
    [InlineData("?maxYear=2000", "Miata")]
    [InlineData("?minYear=2018&maxYear=2018", "Tacoma")]
    public async Task SearchAndYearFilters_ReturnOnlyMatches(string query, string expected)
    {
        var (client, _) = await SeedAsync();

        var page = await ListAsync(client, query);

        var names = expected.Length == 0 ? Array.Empty<string>() : expected.Split(',');
        Assert.Equal(names, page.Items.Select(item => item.Nickname));
        Assert.Equal(names.Length, page.TotalCount);
    }

    [Fact]
    public async Task LookupFilters_NarrowByIdAndCombineWithOtherFilters()
    {
        var (client, lookups) = await SeedAsync();
        var truck = IdOf(lookups.VehicleTypes, "Truck");
        var gasoline = IdOf(lookups.FuelTypes, "Gasoline");
        var manual = IdOf(lookups.Transmissions, "Manual");
        var awd = IdOf(lookups.Drivetrains, "AWD");

        Assert.Equal(["R1T", "Tacoma"], (await ListAsync(client, $"?vehicleTypeId={truck}")).Items.Select(item => item.Nickname));
        Assert.Equal(["Miata", "Tacoma"], (await ListAsync(client, $"?fuelTypeId={gasoline}")).Items.Select(item => item.Nickname));
        Assert.Equal(["Miata"], (await ListAsync(client, $"?transmissionId={manual}")).Items.Select(item => item.Nickname));
        Assert.Equal(["R1T"], (await ListAsync(client, $"?drivetrainId={awd}")).Items.Select(item => item.Nickname));
        Assert.Equal(["Tacoma"], (await ListAsync(client, $"?vehicleTypeId={truck}&fuelTypeId={gasoline}&minYear=2010&make=toyota")).Items.Select(item => item.Nickname));
        Assert.Empty((await ListAsync(client, $"?transmissionId={manual}&drivetrainId={awd}")).Items);
    }

    [Theory]
    [InlineData("?minYear=2020&maxYear=2010")]
    [InlineData("?minYear=1800")]
    [InlineData("?fuelTypeId=abc")]
    [InlineData("?vehicleTypeId=123")]
    public async Task InvalidCriteria_Return400(string query)
    {
        var response = await factory.CreateClientForUser(Alice).GetAsync($"/api/vehicles{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task FilterOptions_ListLookupItemsUsedByTheCallersVehiclesOnly()
    {
        var (client, lookups) = await SeedAsync();
        await factory.InsertLookupItemAsync(Bob, "fuel_type", "Hydrogen");
        var bob = factory.CreateClientForUser(Bob);
        var bobLookups = (await bob.GetFromJsonAsync<LookupsDto>("/api/lookups"))!;
        await CreateAsync(bob, new VehicleRequest { Nickname = "Fuel cell", Make = "Toyota", Model = "Mirai", Year = 2024, FuelTypeId = IdOf(bobLookups.FuelTypes, "Hydrogen") });
        // Hiding an item that is in use must not remove it from the filter choices.
        await client.PutAsJsonAsync($"/api/lookups/{LookupKinds.FuelTypes}/{IdOf(lookups.FuelTypes, "Electric")}/visibility", new SetLookupVisibilityRequest { IsHidden = true });

        var mine = (await client.GetFromJsonAsync<VehicleFilterOptionsDto>("/api/vehicles/filter-options"))!;
        var theirs = (await bob.GetFromJsonAsync<VehicleFilterOptionsDto>("/api/vehicles/filter-options"))!;

        Assert.Equal(["Convertible", "Truck"], mine.VehicleTypes.Select(option => option.Name));
        Assert.Equal(["Electric", "Gasoline"], mine.FuelTypes.Select(option => option.Name));
        Assert.Equal(["Automatic", "Manual"], mine.Transmissions.Select(option => option.Name));
        Assert.Equal(["4WD", "AWD", "RWD"], mine.Drivetrains.Select(option => option.Name));
        Assert.Equal(["Hydrogen"], theirs.FuelTypes.Select(option => option.Name));
        Assert.Empty(theirs.VehicleTypes);
        Assert.Equal(IdOf(lookups.VehicleTypes, "Truck"), mine.VehicleTypes.Single(option => option.Name == "Truck").Id);
    }

    [Fact]
    public async Task OtherUsers_NeverMatchTheCallersLookupFilters()
    {
        var (_, lookups) = await SeedAsync();
        var bob = factory.CreateClientForUser(Bob);

        var page = await ListAsync(bob, $"?vehicleTypeId={IdOf(lookups.VehicleTypes, "Truck")}&minYear=1900");

        Assert.Empty(page.Items);
    }
}
