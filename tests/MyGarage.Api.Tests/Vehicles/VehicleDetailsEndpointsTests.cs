using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Lookups;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

public class VehicleDetailsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static VehicleRequest Minimal() => new() { Nickname = "Daily", Make = "Ford", Model = "F-150", Year = 2020 };

    private static async Task<LookupsDto> LookupsAsync(HttpClient client, bool includeHidden = false) =>
        (await client.GetFromJsonAsync<LookupsDto>($"/api/lookups?includeHidden={includeHidden.ToString().ToLowerInvariant()}"))!;

    private static Guid IdOf(IEnumerable<LookupItemDto> items, string name) => items.Single(item => item.Name == name).Id;

    private static async Task<string[]> ErrorFieldsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        return [.. problem!.Errors.Keys];
    }

    [Fact]
    public async Task Create_AllFields_ReturnsEveryValueWithLookupNames()
    {
        var client = factory.CreateClientForUser(Alice);
        var lookups = await LookupsAsync(client);
        var request = new VehicleRequest
        {
            Nickname = "Blue Streak",
            Make = "Subaru",
            Model = "Outback",
            Trim = "Limited",
            Year = 2019,
            Vin = " 1hgcm82633a004352 ",
            VehicleTypeId = IdOf(lookups.VehicleTypes, "Wagon"),
            Ownership = OwnershipType.Leased,
            PurchasedOn = new DateOnly(2024, 3, 15),
            LeaseEndsOn = new DateOnly(2027, 3, 14),
            Engine = "2.5L flat-4",
            Horsepower = 182,
            FuelTypeId = IdOf(lookups.FuelTypes, "Gasoline"),
            TransmissionId = IdOf(lookups.Transmissions, "Automatic"),
            Mileage = 42_000,
            Color = "Blue",
            Seats = 5,
            Doors = 4,
            DrivetrainId = IdOf(lookups.Drivetrains, "AWD"),
            WheelSize = 17.5m,
            TireSize = "225/60R17",
        };

        var response = await client.PostAsJsonAsync("/api/vehicles", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
        var read = (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{created.Id}"))!;
        foreach (var dto in new[] { created, read })
        {
            Assert.Equal(2019, dto.Year);
            Assert.Equal("1HGCM82633A004352", dto.Vin);
            Assert.Equal("Wagon", dto.VehicleType!.Name);
            Assert.Equal(request.VehicleTypeId, dto.VehicleType.Id);
            Assert.Equal(OwnershipType.Leased, dto.Ownership);
            Assert.Equal(new DateOnly(2024, 3, 15), dto.PurchasedOn);
            Assert.Equal(new DateOnly(2027, 3, 14), dto.LeaseEndsOn);
            Assert.Equal("2.5L flat-4", dto.Engine);
            Assert.Equal(182, dto.Horsepower);
            Assert.Equal("Gasoline", dto.FuelType!.Name);
            Assert.Equal("Automatic", dto.Transmission!.Name);
            Assert.Equal(5, dto.Seats);
            Assert.Equal("AWD", dto.Drivetrain!.Name);
            Assert.Equal(17.5m, dto.WheelSize);
            Assert.Equal("225/60R17", dto.TireSize);
        }
    }

    [Fact]
    public async Task Create_OnlyRequiredFields_LeavesOptionalFieldsNull()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsJsonAsync("/api/vehicles", Minimal());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
        Assert.Equal(2020, created.Year);
        Assert.Null(created.Vin);
        Assert.Null(created.VehicleType);
        Assert.Null(created.Ownership);
        Assert.Null(created.PurchasedOn);
        Assert.Null(created.LeaseEndsOn);
        Assert.Null(created.Engine);
        Assert.Null(created.Horsepower);
        Assert.Null(created.FuelType);
        Assert.Null(created.Transmission);
        Assert.Null(created.Seats);
        Assert.Null(created.Drivetrain);
        Assert.Null(created.WheelSize);
        Assert.Null(created.TireSize);
    }

    public static TheoryData<string, string> InvalidBodies => new()
    {
        { """{"nickname":"n","make":"m","model":"x"}""", "Year" },
        { """{"nickname":"n","make":"m","model":"x","year":1850}""", "Year" },
        { """{"nickname":"n","make":"m","model":"x","year":2020,"vin":"SHORT"}""", "Vin" },
        { """{"nickname":"n","make":"m","model":"x","year":2020,"horsepower":0}""", "Horsepower" },
        { """{"nickname":"n","make":"m","model":"x","year":2020,"seats":25}""", "Seats" },
        { """{"nickname":"n","make":"m","model":"x","year":2020,"wheelSize":5}""", "WheelSize" },
        { """{"nickname":"n","make":"m","model":"x","year":2020,"purchasedOn":"2999-01-01"}""", "PurchasedOn" },
        { """{"nickname":"n","make":"m","model":"x","year":2020,"ownership":"Purchased","leaseEndsOn":"2030-01-01"}""", "LeaseEndsOn" },
        { """{"nickname":"n","make":"m","model":"x","year":2020,"ownership":"Leased","purchasedOn":"2025-05-01","leaseEndsOn":"2025-04-30"}""", "LeaseEndsOn" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Create_InvalidDetails_Returns400KeyedByThePropertyAndStoresNothing(string body, string expectedField)
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsync("/api/vehicles", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Contains(await ErrorFieldsAsync(response), key => key.Equals(expectedField, StringComparison.OrdinalIgnoreCase));
        Assert.Empty((await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles"))!.Items);
    }

    [Fact]
    public async Task Create_LookupIdOfTheWrongKind_IsRejectedOnThatField()
    {
        var client = factory.CreateClientForUser(Alice);
        var lookups = await LookupsAsync(client);

        var response = await client.PostAsJsonAsync("/api/vehicles", Minimal() with { VehicleTypeId = IdOf(lookups.FuelTypes, "Diesel") });

        Assert.Contains(await ErrorFieldsAsync(response), key => key.Equals("VehicleTypeId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Create_UnknownAndForeignCustomItems_GetTheSameRejection()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bobsItem = await factory.InsertLookupItemAsync(Bob, "fuel_type", "Bob's special");

        var unknown = await alice.PostAsJsonAsync("/api/vehicles", Minimal() with { FuelTypeId = Guid.NewGuid() });
        var foreign = await alice.PostAsJsonAsync("/api/vehicles", Minimal() with { FuelTypeId = bobsItem });

        var unknownProblem = (await unknown.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        var foreignProblem = (await foreign.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal(unknownProblem.Errors.Single().Key, foreignProblem.Errors.Single().Key);
        Assert.Equal(unknownProblem.Errors.Single().Value, foreignProblem.Errors.Single().Value);
    }

    [Fact]
    public async Task Create_OwnCustomItem_IsAccepted()
    {
        var client = factory.CreateClientForUser(Alice);
        var custom = await factory.InsertLookupItemAsync(Alice, "drivetrain", "Rear-mid");

        var response = await client.PostAsJsonAsync("/api/vehicles", Minimal() with { DrivetrainId = custom });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Rear-mid", (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!.Drivetrain!.Name);
    }

    [Fact]
    public async Task Put_ReplacesAllNewFieldsAndClearsOmittedOnes()
    {
        var client = factory.CreateClientForUser(Alice);
        var lookups = await LookupsAsync(client);
        var created = (await (await client.PostAsJsonAsync("/api/vehicles", Minimal() with
        {
            Vin = "1HGCM82633A004352", Engine = "old", FuelTypeId = IdOf(lookups.FuelTypes, "Gasoline"), Seats = 4,
        })).Content.ReadFromJsonAsync<VehicleDetailDto>())!;

        var response = await client.PutAsJsonAsync($"/api/vehicles/{created.Id}", Minimal() with
        {
            Year = 2022, Engine = "new", FuelTypeId = IdOf(lookups.FuelTypes, "Electric"),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
        Assert.Equal(2022, updated.Year);
        Assert.Equal("new", updated.Engine);
        Assert.Equal("Electric", updated.FuelType!.Name);
        Assert.Null(updated.Vin);
        Assert.Null(updated.Seats);
    }

    [Fact]
    public async Task Put_InvalidDetails_Returns400AndKeepsStoredData()
    {
        var client = factory.CreateClientForUser(Alice);
        var created = (await (await client.PostAsJsonAsync("/api/vehicles", Minimal() with { Engine = "keep" })).Content.ReadFromJsonAsync<VehicleDetailDto>())!;

        var response = await client.PutAsJsonAsync($"/api/vehicles/{created.Id}", Minimal() with { Engine = "changed", Vin = "BAD" });

        Assert.Contains(await ErrorFieldsAsync(response), key => key.Equals("Vin", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("keep", (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{created.Id}"))!.Engine);
    }

    private static Task<HttpResponseMessage> SetHiddenAsync(HttpClient client, string kind, Guid id, bool isHidden) =>
        client.PutAsJsonAsync($"/api/lookups/{kind}/{id}/visibility", new SetLookupVisibilityRequest { IsHidden = isHidden });

    [Fact]
    public async Task Create_WithAnItemTheCallerHasHidden_IsRejectedOnThatField()
    {
        var client = factory.CreateClientForUser(Alice);
        var lookups = await LookupsAsync(client);
        var coupe = IdOf(lookups.VehicleTypes, "Coupe");
        await SetHiddenAsync(client, LookupKinds.VehicleTypes, coupe, true);

        var response = await client.PostAsJsonAsync("/api/vehicles", Minimal() with { VehicleTypeId = coupe });

        Assert.Contains(await ErrorFieldsAsync(response), key => key.Equals("VehicleTypeId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Hiding_AnItemInUse_KeepsTheVehicleUnchangedAndStillShowsIt()
    {
        var client = factory.CreateClientForUser(Alice);
        var lookups = await LookupsAsync(client);
        var coupe = IdOf(lookups.VehicleTypes, "Coupe");
        var created = (await (await client.PostAsJsonAsync("/api/vehicles", Minimal() with { VehicleTypeId = coupe })).Content.ReadFromJsonAsync<VehicleDetailDto>())!;

        await SetHiddenAsync(client, LookupKinds.VehicleTypes, coupe, true);

        var read = (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{created.Id}"))!;
        Assert.Equal("Coupe", read.VehicleType!.Name);
        Assert.Equal(coupe, read.VehicleType.Id);
        Assert.Equal(created.UpdatedAt, read.UpdatedAt);
    }

    [Fact]
    public async Task Put_KeepingAHiddenItemAlreadyOnTheVehicle_Succeeds()
    {
        var client = factory.CreateClientForUser(Alice);
        var lookups = await LookupsAsync(client);
        var coupe = IdOf(lookups.VehicleTypes, "Coupe");
        var created = (await (await client.PostAsJsonAsync("/api/vehicles", Minimal() with { VehicleTypeId = coupe })).Content.ReadFromJsonAsync<VehicleDetailDto>())!;
        await SetHiddenAsync(client, LookupKinds.VehicleTypes, coupe, true);

        var response = await client.PutAsJsonAsync($"/api/vehicles/{created.Id}", Minimal() with { VehicleTypeId = coupe, Engine = "edited" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
        Assert.Equal("Coupe", updated.VehicleType!.Name);
        Assert.Equal("edited", updated.Engine);
    }

    [Fact]
    public async Task Put_SwitchingToADifferentHiddenItem_IsRejected()
    {
        var client = factory.CreateClientForUser(Alice);
        var lookups = await LookupsAsync(client);
        var coupe = IdOf(lookups.VehicleTypes, "Coupe");
        var truck = IdOf(lookups.VehicleTypes, "Truck");
        var created = (await (await client.PostAsJsonAsync("/api/vehicles", Minimal() with { VehicleTypeId = coupe })).Content.ReadFromJsonAsync<VehicleDetailDto>())!;
        await SetHiddenAsync(client, LookupKinds.VehicleTypes, coupe, true);
        await SetHiddenAsync(client, LookupKinds.VehicleTypes, truck, true);

        var response = await client.PutAsJsonAsync($"/api/vehicles/{created.Id}", Minimal() with { VehicleTypeId = truck });

        Assert.Contains(await ErrorFieldsAsync(response), key => key.Equals("VehicleTypeId", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Coupe", (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{created.Id}"))!.VehicleType!.Name);
    }
}
