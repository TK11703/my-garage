using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Tests.Common;
using MyGarage.Api.Vehicles;
using MyGarage.Shared.Vehicles;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.Vehicles;

public class VehicleLatestOdometerValidatorTests
{
    private static readonly DateTimeOffset Today = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly VehicleRequestValidator validator = new(new FixedTimeProvider(Today));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private bool IsValid(int? latest) =>
        validator.Validate(new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020, LatestOdometerReading = latest }).IsValid;

    [Theory]
    [InlineData(null, true)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(9_999_999, true)]
    [InlineData(10_000_000, false)]
    public void LatestOdometerReading_OptionalWholeNumberBetween0And9999999(int? value, bool valid) =>
        Assert.Equal(valid, IsValid(value));

    [Fact]
    public void LatestOdometerReading_HasASpecificMessage()
    {
        var result = validator.Validate(new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020, LatestOdometerReading = -1 });

        Assert.Equal("Latest odometer reading must be between 0 and 9,999,999.", result.Errors.Single().ErrorMessage);
        Assert.Equal("LatestOdometerReading", result.Errors.Single().PropertyName);
    }
}

public class VehicleLatestOdometerMapperTests
{
    [Fact]
    public void ToRecord_AndToDetailDto_CarryTheLatestOdometerReading()
    {
        var now = DateTimeOffset.UtcNow;
        var record = VehicleMapper.ToRecord(
            new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020, LatestOdometerReading = 61_000 }, "owner-1", Guid.NewGuid(), now);

        var dto = VehicleMapper.ToDetailDto(record, [], []);

        Assert.Equal(61_000, record.LatestOdometerReading);
        Assert.Equal(61_000, dto.LatestOdometerReading);
    }

    [Fact]
    public void ToRecord_WithoutAReading_LeavesItNull()
    {
        var record = VehicleMapper.ToRecord(new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020 }, "owner-1", Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Null(record.LatestOdometerReading);
    }
}

public class VehicleLatestOdometerEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<int?> CardMileageAsync(HttpClient client, Guid vehicleId, string query = "") =>
        (await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>($"/api/vehicles{query}"))!.Items.SingleOrDefault(item => item.Id == vehicleId)?.Mileage;

    [Fact]
    public async Task CreateAndUpdate_RoundTripTheLatestOdometerReading()
    {
        var client = factory.CreateClientForUser(Alice);

        var created = await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020, LatestOdometerReading = 60_000 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var vehicle = (await created.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
        Assert.Equal(60_000, vehicle.LatestOdometerReading);

        var updated = await client.PutAsJsonAsync($"/api/vehicles/{vehicle.Id}", new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020, LatestOdometerReading = 62_500 });
        Assert.Equal(62_500, (await updated.Content.ReadFromJsonAsync<VehicleDetailDto>())!.LatestOdometerReading);
        Assert.Equal(62_500, (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!.LatestOdometerReading);
    }

    [Fact]
    public async Task AnInvalidReading_Returns400KeyedByTheField()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020, LatestOdometerReading = -5 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(problem!.Errors.Keys, key => key.Equals("latestOdometerReading", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TheCardMileage_FollowsTheServiceRecords()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = (await (await client.PostAsJsonAsync(
                "/api/vehicles", new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020, Mileage = 20_000, LatestOdometerReading = 60_000 }))
            .Content.ReadFromJsonAsync<VehicleDetailDto>())!;
        Assert.Equal(60_000, await CardMileageAsync(client, vehicle.Id));

        var record = await CreateRecordAsync(client, vehicle.Id, odometer: 70_000);
        Assert.Equal(70_000, await CardMileageAsync(client, vehicle.Id));
        Assert.Equal(vehicle.Id, (await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles?minMileage=65000"))!.Items.Single().Id);

        await client.DeleteAsync($"/api/vehicles/{vehicle.Id}/service-records/{record.Id}");
        Assert.Equal(60_000, await CardMileageAsync(client, vehicle.Id));
        Assert.Empty((await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles?minMileage=65000"))!.Items);
    }

    [Fact]
    public async Task AVehicleWithoutRecordsOrAReading_KeepsItsPurchaseMileage()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = (await (await client.PostAsJsonAsync(
                "/api/vehicles", new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020, Mileage = 20_000 }))
            .Content.ReadFromJsonAsync<VehicleDetailDto>())!;

        Assert.Equal(20_000, await CardMileageAsync(client, vehicle.Id));
    }
}
