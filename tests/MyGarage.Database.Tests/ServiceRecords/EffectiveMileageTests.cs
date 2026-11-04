using MyGarage.Database.Common;
using MyGarage.Database.ServiceRecords;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.ServiceRecords;

// The garage card and the mileage filters use the greatest of the latest-dated record's odometer, the latest odometer reading, and the purchase mileage.
[Collection(DatabaseCollection.Name)]
public class EffectiveMileageTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = TestData.Created;

    private async Task<(NpgsqlDataSource DataSource, VehicleRepository Vehicles, ServiceRecordRepository Records)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        return (dataSource, new VehicleRepository(dataSource, scripts), new ServiceRecordRepository(dataSource, scripts));
    }

    private static async Task<VehicleRecord> InsertAsync(VehicleRepository vehicles, int? mileage = null, int? latest = null, string ownerId = "owner-1")
    {
        var vehicle = TestData.Vehicle(ownerId, mileage: mileage) with { LatestOdometerReading = latest };
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        return vehicle;
    }

    private static Task AddRecordAsync(ServiceRecordRepository records, VehicleRecord vehicle, DateOnly date, int odometer, DateTimeOffset? at = null) =>
        records.AddAsync(vehicle.OwnerId, vehicle.Id, Guid.CreateVersion7(), date, odometer, null, null, at ?? T0, CancellationToken.None);

    private static async Task<int?> MileageAsync(VehicleRepository vehicles, VehicleRecord vehicle) =>
        (await vehicles.ListPageAsync(vehicle.OwnerId, VehicleSearchCriteria.None, 1, 50, CancellationToken.None))
        .Items.Single(item => item.Id == vehicle.Id).Mileage;

    [Fact]
    public async Task LatestOdometerReading_RoundTripsThroughInsertGetAndUpdate()
    {
        var (_, vehicles, _) = await CreateAsync();
        var vehicle = await InsertAsync(vehicles, latest: 60_000);

        var stored = await vehicles.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None);
        Assert.Equal(60_000, stored!.LatestOdometerReading);

        Assert.True(await vehicles.UpdateAsync(stored with { LatestOdometerReading = 61_000 }, CancellationToken.None));
        Assert.Equal(61_000, (await vehicles.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None))!.LatestOdometerReading);

        Assert.True(await vehicles.UpdateAsync(stored with { LatestOdometerReading = null }, CancellationToken.None));
        Assert.Null((await vehicles.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None))!.LatestOdometerReading);
    }

    [Fact]
    public async Task WithNothingSet_TheMileageIsNull()
    {
        var (_, vehicles, _) = await CreateAsync();
        var vehicle = await InsertAsync(vehicles);

        Assert.Null(await MileageAsync(vehicles, vehicle));
    }

    [Fact]
    public async Task OnlyThePurchaseMileage_IsUsedForVehiclesWithoutRecords()
    {
        var (_, vehicles, _) = await CreateAsync();
        var vehicle = await InsertAsync(vehicles, mileage: 20_000);

        Assert.Equal(20_000, await MileageAsync(vehicles, vehicle));
    }

    [Fact]
    public async Task TheLatestOdometerReading_BeatsAnEarlierPurchaseMileage()
    {
        var (_, vehicles, _) = await CreateAsync();
        var vehicle = await InsertAsync(vehicles, mileage: 20_000, latest: 60_000);

        Assert.Equal(60_000, await MileageAsync(vehicles, vehicle));
    }

    [Fact]
    public async Task AHigherServiceRecordOdometer_Wins_AndDeletingItFallsBack()
    {
        var (_, vehicles, records) = await CreateAsync();
        var vehicle = await InsertAsync(vehicles, mileage: 20_000, latest: 60_000);
        var detail = await records.AddAsync("owner-1", vehicle.Id, Guid.CreateVersion7(), new DateOnly(2026, 5, 1), 70_000, null, null, T0, CancellationToken.None);

        Assert.Equal(70_000, await MileageAsync(vehicles, vehicle));

        await records.DeleteAsync("owner-1", vehicle.Id, detail!.Record.Id, CancellationToken.None);
        Assert.Equal(60_000, await MileageAsync(vehicles, vehicle));
    }

    [Fact]
    public async Task ALowerServiceRecordOdometer_DoesNotLowerTheMileage()
    {
        var (_, vehicles, records) = await CreateAsync();
        var vehicle = await InsertAsync(vehicles, latest: 60_000);
        await AddRecordAsync(records, vehicle, new DateOnly(2026, 5, 1), 50_000);

        Assert.Equal(60_000, await MileageAsync(vehicles, vehicle));
    }

    [Fact]
    public async Task TheLatestDatedRecordIsTheOneUsed_NotTheHighestOdometerOrTheNewestCreated()
    {
        var (_, vehicles, records) = await CreateAsync();
        var vehicle = await InsertAsync(vehicles);
        await AddRecordAsync(records, vehicle, new DateOnly(2026, 6, 1), 30_000, T0);
        await AddRecordAsync(records, vehicle, new DateOnly(2026, 1, 1), 90_000, T0.AddMinutes(5));

        Assert.Equal(30_000, await MileageAsync(vehicles, vehicle));
    }

    [Fact]
    public async Task TwoRecordsOnTheSameDate_UseTheNewestCreated()
    {
        var (_, vehicles, records) = await CreateAsync();
        var vehicle = await InsertAsync(vehicles);
        await AddRecordAsync(records, vehicle, new DateOnly(2026, 6, 1), 40_000, T0);
        await AddRecordAsync(records, vehicle, new DateOnly(2026, 6, 1), 35_000, T0.AddMinutes(1));

        Assert.Equal(35_000, await MileageAsync(vehicles, vehicle));
    }

    [Fact]
    public async Task EditingTheLatestRecord_ChangesTheMileage()
    {
        var (_, vehicles, records) = await CreateAsync();
        var vehicle = await InsertAsync(vehicles);
        var detail = await records.AddAsync("owner-1", vehicle.Id, Guid.CreateVersion7(), new DateOnly(2026, 6, 1), 40_000, null, null, T0, CancellationToken.None);

        await records.UpdateAsync("owner-1", vehicle.Id, detail!.Record.Id, new DateOnly(2026, 6, 1), 41_500, null, null, T0, CancellationToken.None);

        Assert.Equal(41_500, await MileageAsync(vehicles, vehicle));
    }

    [Fact]
    public async Task TheMileageFiltersAndTheCount_UseTheSameValueAsTheCard()
    {
        var (_, vehicles, records) = await CreateAsync();
        var withRecord = await InsertAsync(vehicles, mileage: 10_000);
        await AddRecordAsync(records, withRecord, new DateOnly(2026, 5, 1), 70_000);
        var withoutRecord = await InsertAsync(vehicles, mileage: 10_000);

        var atLeast = await vehicles.ListPageAsync(
            "owner-1", new VehicleSearchCriteria { MinMileage = 65_000 }, 1, 50, CancellationToken.None);
        var atMost = await vehicles.ListPageAsync(
            "owner-1", new VehicleSearchCriteria { MaxMileage = 65_000 }, 1, 50, CancellationToken.None);

        Assert.Equal([withRecord.Id], atLeast.Items.Select(item => item.Id));
        Assert.Equal(1, atLeast.TotalCount);
        Assert.Equal([withoutRecord.Id], atMost.Items.Select(item => item.Id));
        Assert.Equal(1, atMost.TotalCount);
    }

    [Fact]
    public async Task AnotherOwnersRecords_NeverInfluenceAVehicle()
    {
        var (_, vehicles, records) = await CreateAsync();
        var mine = await InsertAsync(vehicles, mileage: 1_000, ownerId: "owner-1");
        var theirs = await InsertAsync(vehicles, mileage: 2_000, ownerId: "owner-2");
        await AddRecordAsync(records, theirs, new DateOnly(2026, 5, 1), 99_000);

        Assert.Equal(1_000, await MileageAsync(vehicles, mine));
        Assert.Equal(99_000, await MileageAsync(vehicles, theirs));
    }
}
