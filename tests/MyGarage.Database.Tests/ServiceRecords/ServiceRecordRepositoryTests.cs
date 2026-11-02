using MyGarage.Database.Common;
using MyGarage.Database.ServiceRecords;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.ServiceRecords;

[Collection(DatabaseCollection.Name)]
public class ServiceRecordRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = TestData.Created;

    private async Task<(NpgsqlDataSource DataSource, VehicleRepository Vehicles, ServiceRecordRepository Records)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        return (dataSource, new VehicleRepository(dataSource, scripts), new ServiceRecordRepository(dataSource, scripts));
    }

    private static async Task<VehicleRecord> InsertVehicleAsync(VehicleRepository vehicles, string ownerId)
    {
        var vehicle = TestData.Vehicle(ownerId);
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        return vehicle;
    }

    private static Task<ServiceRecordDetailRecord?> AddAsync(
        ServiceRecordRepository records, string ownerId, Guid vehicleId, DateOnly date, int odometer = 1000, string? shop = null, DateTimeOffset? at = null) =>
        records.AddAsync(ownerId, vehicleId, Guid.CreateVersion7(), date, odometer, shop, at ?? T0, CancellationToken.None);

    [Fact]
    public async Task Add_StoresTheRecordWithNoLineItemsAndAudit()
    {
        var (_, vehicles, records) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        var detail = await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2026, 5, 1), 45000, "Joe's Garage");

        Assert.NotNull(detail);
        Assert.Equal(vehicle.Id, detail.Record.VehicleId);
        Assert.Equal(new DateOnly(2026, 5, 1), detail.Record.DateServiced);
        Assert.Equal(45000, detail.Record.OdometerReading);
        Assert.Equal("Joe's Garage", detail.Record.ShopOrPerson);
        Assert.Equal(0, detail.Record.LineItemCount);
        Assert.Equal(0m, detail.Record.TotalCost);
        Assert.Empty(detail.LineItems);
        Assert.Equal(T0, detail.Record.CreatedAt);
        Assert.Equal(T0, detail.Record.UpdatedAt);
        Assert.Equal("owner-1", detail.Record.CreatedBy);
        Assert.Equal("owner-1", detail.Record.UpdatedBy);
    }

    [Fact]
    public async Task Add_WithoutShop_StoresNull()
    {
        var (_, vehicles, records) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        var detail = await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2026, 5, 1));
        var loaded = await records.GetAsync("owner-1", vehicle.Id, detail!.Record.Id, CancellationToken.None);

        Assert.Null(loaded!.Record.ShopOrPerson);
    }

    [Fact]
    public async Task Add_AnotherOwnersVehicle_ReturnsNullAndInsertsNothing()
    {
        var (dataSource, vehicles, records) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        var detail = await AddAsync(records, "owner-2", vehicle.Id, new DateOnly(2026, 5, 1));

        Assert.Null(detail);
        Assert.Equal(0, await ServiceRecordTestData.CountAsync(dataSource, "service_records"));
    }

    [Fact]
    public async Task Add_UnknownVehicle_ReturnsNull()
    {
        var (_, _, records) = await CreateAsync();

        Assert.Null(await AddAsync(records, "owner-1", Guid.NewGuid(), new DateOnly(2026, 5, 1)));
    }

    [Fact]
    public async Task ListPage_OrdersByDateServicedThenNewestCreatedFirst()
    {
        var (_, vehicles, records) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var older = await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2025, 1, 1), at: T0);
        var newest = await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2026, 6, 1), at: T0.AddMinutes(1));
        var sameDayFirst = await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2026, 2, 1), at: T0.AddMinutes(2));
        var sameDayLater = await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2026, 2, 1), at: T0.AddMinutes(3));

        var page = await records.ListPageAsync("owner-1", vehicle.Id, 1, 10, CancellationToken.None);

        Assert.NotNull(page);
        Assert.Equal(
            [newest!.Record.Id, sameDayLater!.Record.Id, sameDayFirst!.Record.Id, older!.Record.Id],
            page.Items.Select(record => record.Id));
        Assert.Equal(4, page.TotalCount);
    }

    [Fact]
    public async Task ListPage_PagesAndReportsTotals_AndAPastTheLastPageIsEmpty()
    {
        var (_, vehicles, records) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        for (var day = 1; day <= 5; day++)
        {
            await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2026, 1, day));
        }

        var second = await records.ListPageAsync("owner-1", vehicle.Id, 2, 2, CancellationToken.None);
        var past = await records.ListPageAsync("owner-1", vehicle.Id, 9, 2, CancellationToken.None);

        Assert.Equal([new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 2)], second!.Items.Select(record => record.DateServiced));
        Assert.Equal(3, second.TotalPages);
        Assert.Equal(5, second.TotalCount);
        Assert.Empty(past!.Items);
    }

    [Fact]
    public async Task ListPage_NeverMixesVehicles()
    {
        var (_, vehicles, records) = await CreateAsync();
        var vehicleA = await InsertVehicleAsync(vehicles, "owner-1");
        var vehicleB = await InsertVehicleAsync(vehicles, "owner-1");
        await AddAsync(records, "owner-1", vehicleA.Id, new DateOnly(2026, 1, 1));
        await AddAsync(records, "owner-1", vehicleB.Id, new DateOnly(2026, 1, 2));

        var page = await records.ListPageAsync("owner-1", vehicleA.Id, 1, 10, CancellationToken.None);

        Assert.Equal([vehicleA.Id], page!.Items.Select(record => record.VehicleId));
    }

    [Fact]
    public async Task ListPage_AnotherOwnersOrUnknownVehicle_ReturnsNull()
    {
        var (_, vehicles, records) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2026, 1, 1));

        Assert.Null(await records.ListPageAsync("owner-2", vehicle.Id, 1, 10, CancellationToken.None));
        Assert.Null(await records.ListPageAsync("owner-1", Guid.NewGuid(), 1, 10, CancellationToken.None));
    }

    [Fact]
    public async Task Get_ReturnsTheRecordOnlyThroughItsOwnVehicleAndOwner()
    {
        var (_, vehicles, records) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var otherVehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var detail = await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2026, 1, 1));
        var id = detail!.Record.Id;

        Assert.NotNull(await records.GetAsync("owner-1", vehicle.Id, id, CancellationToken.None));
        Assert.Null(await records.GetAsync("owner-1", otherVehicle.Id, id, CancellationToken.None));
        Assert.Null(await records.GetAsync("owner-2", vehicle.Id, id, CancellationToken.None));
        Assert.Null(await records.GetAsync("owner-1", vehicle.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Update_ChangesTheValuesAndAuditButNotCreatedOrLineItems()
    {
        var (dataSource, vehicles, records) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var detail = await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2026, 1, 1), 100, "Old shop");
        var id = detail!.Record.Id;
        await ServiceRecordTestData.InsertLineItemAsync(dataSource, id, flatCost: 25m);

        var updated = await records.UpdateAsync(
            "owner-1", vehicle.Id, id, new DateOnly(2026, 3, 3), 250, null, T0.AddDays(1), CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(new DateOnly(2026, 3, 3), updated.Record.DateServiced);
        Assert.Equal(250, updated.Record.OdometerReading);
        Assert.Null(updated.Record.ShopOrPerson);
        Assert.Equal(T0, updated.Record.CreatedAt);
        Assert.Equal("owner-1", updated.Record.CreatedBy);
        Assert.Equal(T0.AddDays(1), updated.Record.UpdatedAt);
        Assert.Equal("owner-1", updated.Record.UpdatedBy);
        Assert.Equal(1, updated.Record.LineItemCount);
        Assert.Equal(25m, updated.Record.TotalCost);
    }

    [Fact]
    public async Task Update_NotFoundCases_ReturnNullAndChangeNothing()
    {
        var (_, vehicles, records) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var otherVehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var id = (await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2026, 1, 1), 100, "Shop"))!.Record.Id;

        Assert.Null(await records.UpdateAsync("owner-1", vehicle.Id, Guid.NewGuid(), new DateOnly(2026, 2, 2), 1, "x", T0, CancellationToken.None));
        Assert.Null(await records.UpdateAsync("owner-1", otherVehicle.Id, id, new DateOnly(2026, 2, 2), 1, "x", T0, CancellationToken.None));
        Assert.Null(await records.UpdateAsync("owner-2", vehicle.Id, id, new DateOnly(2026, 2, 2), 1, "x", T0, CancellationToken.None));

        var unchanged = await records.GetAsync("owner-1", vehicle.Id, id, CancellationToken.None);
        Assert.Equal(new DateOnly(2026, 1, 1), unchanged!.Record.DateServiced);
        Assert.Equal(100, unchanged.Record.OdometerReading);
        Assert.Equal("Shop", unchanged.Record.ShopOrPerson);
    }

    [Fact]
    public async Task Update_AChangedDate_ChangesTheRecordsPlaceInTheList()
    {
        var (_, vehicles, records) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var first = (await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2026, 1, 1)))!.Record.Id;
        var second = (await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2026, 2, 1)))!.Record.Id;

        await records.UpdateAsync("owner-1", vehicle.Id, first, new DateOnly(2026, 3, 1), 1000, null, T0, CancellationToken.None);

        var page = await records.ListPageAsync("owner-1", vehicle.Id, 1, 10, CancellationToken.None);
        Assert.Equal([first, second], page!.Items.Select(record => record.Id));
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatRecordWithItsLineItemsAndParts()
    {
        var (dataSource, vehicles, records) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var doomed = (await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2026, 1, 1)))!.Record.Id;
        var kept = (await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2026, 2, 1)))!.Record.Id;
        var doomedLine = await ServiceRecordTestData.InsertLineItemAsync(dataSource, doomed);
        await ServiceRecordTestData.InsertLineItemAsync(dataSource, kept);
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await Dapper.SqlMapper.ExecuteAsync(
                connection,
                "INSERT INTO service_line_item_parts (id, line_item_id, position, name, quantity, unit_price) VALUES (gen_random_uuid(), @doomedLine, 1, 'Filter', 1, 1)",
                new { doomedLine });
        }

        var deleted = await records.DeleteAsync("owner-1", vehicle.Id, doomed, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await records.GetAsync("owner-1", vehicle.Id, doomed, CancellationToken.None));
        Assert.NotNull(await records.GetAsync("owner-1", vehicle.Id, kept, CancellationToken.None));
        Assert.Equal(1, await ServiceRecordTestData.CountAsync(dataSource, "service_records"));
        Assert.Equal(1, await ServiceRecordTestData.CountAsync(dataSource, "service_line_items"));
        Assert.Equal(0, await ServiceRecordTestData.CountAsync(dataSource, "service_line_item_parts"));
    }

    [Fact]
    public async Task Delete_NotFoundCases_ReturnFalseAndRemoveNothing()
    {
        var (dataSource, vehicles, records) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var otherVehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var id = (await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2026, 1, 1)))!.Record.Id;

        Assert.False(await records.DeleteAsync("owner-1", vehicle.Id, Guid.NewGuid(), CancellationToken.None));
        Assert.False(await records.DeleteAsync("owner-1", otherVehicle.Id, id, CancellationToken.None));
        Assert.False(await records.DeleteAsync("owner-2", vehicle.Id, id, CancellationToken.None));
        Assert.Equal(1, await ServiceRecordTestData.CountAsync(dataSource, "service_records"));
    }

    [Fact]
    public async Task Truncating_Vehicles_ClearsTheServiceTables()
    {
        var (dataSource, vehicles, records) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var detail = await AddAsync(records, "owner-1", vehicle.Id, new DateOnly(2026, 1, 1));
        await ServiceRecordTestData.InsertLineItemAsync(dataSource, detail!.Record.Id);

        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await using var command = new NpgsqlCommand("TRUNCATE TABLE vehicles CASCADE", connection);
            await command.ExecuteNonQueryAsync();
        }

        Assert.Equal(0, await ServiceRecordTestData.CountAsync(dataSource, "service_records"));
        Assert.Equal(0, await ServiceRecordTestData.CountAsync(dataSource, "service_line_items"));
    }
}
