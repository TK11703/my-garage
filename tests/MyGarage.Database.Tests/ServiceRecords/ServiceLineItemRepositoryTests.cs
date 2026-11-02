using MyGarage.Database.Common;
using MyGarage.Database.ServiceRecords;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.ServiceRecords;

[Collection(DatabaseCollection.Name)]
public class ServiceLineItemRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = TestData.Created;
    private static readonly DateOnly ServiceDate = new(2026, 5, 1);

    private async Task<(NpgsqlDataSource DataSource, VehicleRepository Vehicles, ServiceRecordRepository Records)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        return (dataSource, new VehicleRepository(dataSource, scripts), new ServiceRecordRepository(dataSource, scripts));
    }

    private static async Task<(VehicleRecord Vehicle, Guid RecordId)> InsertRecordAsync(
        VehicleRepository vehicles, ServiceRecordRepository records, string ownerId = "owner-1")
    {
        var vehicle = TestData.Vehicle(ownerId);
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        var detail = await records.AddAsync(ownerId, vehicle.Id, Guid.CreateVersion7(), ServiceDate, 1000, null, T0, CancellationToken.None);
        return (vehicle, detail!.Record.Id);
    }

    private static ServiceLineItemInput Flat(string description = "Tire rotation", decimal cost = 40m) =>
        new(description, cost, null, null, []);

    private static ServiceLineItemInput Breakdown(string description = "Oil change") =>
        new(description, null, 1.5m, 90m, [new ServicePartInput("Oil filter", 1m, 12.50m), new ServicePartInput("Oil", 5m, 8.25m)]);

    private static Task<AddLineItemResult> AddAsync(
        ServiceRecordRepository records, string ownerId, Guid vehicleId, Guid recordId, ServiceLineItemInput input, DateTimeOffset? at = null) =>
        records.AddLineItemAsync(ownerId, vehicleId, recordId, Guid.CreateVersion7(), input, at ?? T0, CancellationToken.None);

    [Fact]
    public async Task Add_FlatCostLine_StoresTheFlatCostAsItsTotal()
    {
        var (_, vehicles, records) = await CreateAsync();
        var (vehicle, recordId) = await InsertRecordAsync(vehicles, records);

        var result = await AddAsync(records, "owner-1", vehicle.Id, recordId, Flat());

        Assert.Equal(AddLineItemOutcome.Created, result.Outcome);
        var line = Assert.Single(result.Detail!.LineItems);
        Assert.Equal("Tire rotation", line.Description);
        Assert.Equal(40m, line.FlatCost);
        Assert.Null(line.LaborHours);
        Assert.Null(line.LaborRate);
        Assert.Equal(40m, line.TotalCost);
        Assert.Empty(line.Parts);
        Assert.Equal(40m, result.Detail.Record.TotalCost);
        Assert.Equal(1, result.Detail.Record.LineItemCount);
    }

    [Fact]
    public async Task Add_BreakdownLine_StoresTheCalculatedTotalAndThePartsInOrder()
    {
        var (_, vehicles, records) = await CreateAsync();
        var (vehicle, recordId) = await InsertRecordAsync(vehicles, records);

        var result = await AddAsync(records, "owner-1", vehicle.Id, recordId, Breakdown());

        var line = Assert.Single(result.Detail!.LineItems);
        Assert.Null(line.FlatCost);
        Assert.Equal(1.5m, line.LaborHours);
        Assert.Equal(90m, line.LaborRate);
        Assert.Equal(188.75m, line.TotalCost);
        Assert.Equal(
            [(1, "Oil filter", 1m, 12.50m), (2, "Oil", 5m, 8.25m)],
            line.Parts.Select(part => (part.Position, part.Name, part.Quantity, part.UnitPrice)));
        Assert.Equal(188.75m, result.Detail.Record.TotalCost);
    }

    [Fact]
    public async Task Get_ReturnsLineItemsInTheOrderTheyWereAddedAndTheSumAsTheTotal()
    {
        var (_, vehicles, records) = await CreateAsync();
        var (vehicle, recordId) = await InsertRecordAsync(vehicles, records);
        await AddAsync(records, "owner-1", vehicle.Id, recordId, Flat("First", 10m), T0);
        await AddAsync(records, "owner-1", vehicle.Id, recordId, Breakdown("Second"), T0.AddMinutes(1));
        await AddAsync(records, "owner-1", vehicle.Id, recordId, Flat("Third", 0m), T0.AddMinutes(2));

        var detail = await records.GetAsync("owner-1", vehicle.Id, recordId, CancellationToken.None);

        Assert.Equal(["First", "Second", "Third"], detail!.LineItems.Select(line => line.Description));
        Assert.Equal(198.75m, detail.Record.TotalCost);
        Assert.Equal(3, detail.Record.LineItemCount);
        Assert.Equal(2, detail.LineItems[1].Parts.Count);
    }

    [Fact]
    public async Task ListPage_ShowsTheLineItemCountAndTotalPerRecord()
    {
        var (_, vehicles, records) = await CreateAsync();
        var (vehicle, recordId) = await InsertRecordAsync(vehicles, records);
        await AddAsync(records, "owner-1", vehicle.Id, recordId, Flat("A", 10m));
        await AddAsync(records, "owner-1", vehicle.Id, recordId, Breakdown());

        var page = await records.ListPageAsync("owner-1", vehicle.Id, 1, 10, CancellationToken.None);

        var record = Assert.Single(page!.Items);
        Assert.Equal(2, record.LineItemCount);
        Assert.Equal(198.75m, record.TotalCost);
    }

    [Fact]
    public async Task Update_FlatToBreakdown_ClearsTheFlatCostAndRecomputes()
    {
        var (_, vehicles, records) = await CreateAsync();
        var (vehicle, recordId) = await InsertRecordAsync(vehicles, records);
        var lineId = Guid.CreateVersion7();
        await records.AddLineItemAsync("owner-1", vehicle.Id, recordId, lineId, Flat(), T0, CancellationToken.None);

        var detail = await records.UpdateLineItemAsync("owner-1", vehicle.Id, recordId, lineId, Breakdown("Changed"), T0.AddDays(1), CancellationToken.None);

        var line = Assert.Single(detail!.LineItems);
        Assert.Equal("Changed", line.Description);
        Assert.Null(line.FlatCost);
        Assert.Equal(188.75m, line.TotalCost);
        Assert.Equal(2, line.Parts.Count);
        Assert.Equal(188.75m, detail.Record.TotalCost);
    }

    [Fact]
    public async Task Update_BreakdownToFlat_RemovesLaborAndParts()
    {
        var (dataSource, vehicles, records) = await CreateAsync();
        var (vehicle, recordId) = await InsertRecordAsync(vehicles, records);
        var lineId = Guid.CreateVersion7();
        await records.AddLineItemAsync("owner-1", vehicle.Id, recordId, lineId, Breakdown(), T0, CancellationToken.None);

        var detail = await records.UpdateLineItemAsync("owner-1", vehicle.Id, recordId, lineId, Flat("Flat now", 55m), T0, CancellationToken.None);

        var line = Assert.Single(detail!.LineItems);
        Assert.Equal(55m, line.FlatCost);
        Assert.Null(line.LaborHours);
        Assert.Null(line.LaborRate);
        Assert.Empty(line.Parts);
        Assert.Equal(55m, line.TotalCost);
        Assert.Equal(0, await ServiceRecordTestData.CountAsync(dataSource, "service_line_item_parts"));
    }

    [Fact]
    public async Task Update_ReplacesThePartsSetAndSetsAuditButNotCreated()
    {
        var (_, vehicles, records) = await CreateAsync();
        var (vehicle, recordId) = await InsertRecordAsync(vehicles, records);
        var lineId = Guid.CreateVersion7();
        await records.AddLineItemAsync("owner-1", vehicle.Id, recordId, lineId, Breakdown(), T0, CancellationToken.None);

        var detail = await records.UpdateLineItemAsync(
            "owner-1", vehicle.Id, recordId, lineId,
            new ServiceLineItemInput("Brakes", null, null, null, [new ServicePartInput("Pads", 2m, 30m)]),
            T0.AddDays(1), CancellationToken.None);

        var line = Assert.Single(detail!.LineItems);
        Assert.Equal([(1, "Pads", 2m, 30m)], line.Parts.Select(part => (part.Position, part.Name, part.Quantity, part.UnitPrice)));
        Assert.Equal(60m, line.TotalCost);
    }

    [Fact]
    public async Task Update_APartViolatingACheck_RollsTheWholeWriteBack()
    {
        var (_, vehicles, records) = await CreateAsync();
        var (vehicle, recordId) = await InsertRecordAsync(vehicles, records);
        var lineId = Guid.CreateVersion7();
        await records.AddLineItemAsync("owner-1", vehicle.Id, recordId, lineId, Breakdown("Original"), T0, CancellationToken.None);

        await Assert.ThrowsAsync<PostgresException>(() => records.UpdateLineItemAsync(
            "owner-1", vehicle.Id, recordId, lineId,
            new ServiceLineItemInput("Broken", null, null, null, [new ServicePartInput("Bad", 0m, 1m)]),
            T0, CancellationToken.None));

        var detail = await records.GetAsync("owner-1", vehicle.Id, recordId, CancellationToken.None);
        var line = Assert.Single(detail!.LineItems);
        Assert.Equal("Original", line.Description);
        Assert.Equal(2, line.Parts.Count);
        Assert.Equal(188.75m, line.TotalCost);
    }

    [Fact]
    public async Task Delete_RemovesTheLineAndItsPartsAndTheTotalFollows()
    {
        var (dataSource, vehicles, records) = await CreateAsync();
        var (vehicle, recordId) = await InsertRecordAsync(vehicles, records);
        var keptId = Guid.CreateVersion7();
        var doomedId = Guid.CreateVersion7();
        await records.AddLineItemAsync("owner-1", vehicle.Id, recordId, keptId, Flat("Kept", 40m), T0, CancellationToken.None);
        await records.AddLineItemAsync("owner-1", vehicle.Id, recordId, doomedId, Breakdown("Doomed"), T0.AddMinutes(1), CancellationToken.None);

        var deleted = await records.DeleteLineItemAsync("owner-1", vehicle.Id, recordId, doomedId, CancellationToken.None);

        Assert.True(deleted);
        var detail = await records.GetAsync("owner-1", vehicle.Id, recordId, CancellationToken.None);
        Assert.Equal(["Kept"], detail!.LineItems.Select(line => line.Description));
        Assert.Equal(40m, detail.Record.TotalCost);
        Assert.Equal(0, await ServiceRecordTestData.CountAsync(dataSource, "service_line_item_parts"));
    }

    [Fact]
    public async Task TheFiftyFirstLineItem_ReturnsLimitReached_AndAnotherRecordIsUnaffected()
    {
        var (_, vehicles, records) = await CreateAsync();
        var (vehicle, recordId) = await InsertRecordAsync(vehicles, records);
        var other = (await records.AddAsync("owner-1", vehicle.Id, Guid.CreateVersion7(), ServiceDate, 1, null, T0, CancellationToken.None))!.Record.Id;
        for (var i = 0; i < 50; i++)
        {
            var added = await AddAsync(records, "owner-1", vehicle.Id, recordId, Flat($"Line {i}", 1m));
            Assert.Equal(AddLineItemOutcome.Created, added.Outcome);
        }

        var over = await AddAsync(records, "owner-1", vehicle.Id, recordId, Flat("One too many"));
        var elsewhere = await AddAsync(records, "owner-1", vehicle.Id, other, Flat("Fine"));

        Assert.Equal(AddLineItemOutcome.LimitReached, over.Outcome);
        Assert.Null(over.Detail);
        Assert.Equal(AddLineItemOutcome.Created, elsewhere.Outcome);
        Assert.Equal(50, (await records.GetAsync("owner-1", vehicle.Id, recordId, CancellationToken.None))!.LineItems.Count);
    }

    [Fact]
    public async Task ConcurrentAdds_NeverExceedFiftyLineItems()
    {
        var (_, vehicles, records) = await CreateAsync();
        var (vehicle, recordId) = await InsertRecordAsync(vehicles, records);
        for (var i = 0; i < 40; i++)
        {
            await AddAsync(records, "owner-1", vehicle.Id, recordId, Flat($"Line {i}", 1m));
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => AddAsync(records, "owner-1", vehicle.Id, recordId, Flat($"Race {i}", 1m))));

        Assert.Equal(10, results.Count(result => result.Outcome == AddLineItemOutcome.Created));
        Assert.Equal(10, results.Count(result => result.Outcome == AddLineItemOutcome.LimitReached));
        Assert.Equal(50, (await records.GetAsync("owner-1", vehicle.Id, recordId, CancellationToken.None))!.LineItems.Count);
    }

    [Fact]
    public async Task DeletingTheVehicle_RemovesItsRecordsLineItemsAndPartsAndLeavesOtherVehiclesAlone()
    {
        var (dataSource, vehicles, records) = await CreateAsync();
        var (doomed, doomedRecord) = await InsertRecordAsync(vehicles, records);
        var (kept, keptRecord) = await InsertRecordAsync(vehicles, records);
        await AddAsync(records, "owner-1", doomed.Id, doomedRecord, Breakdown());
        await AddAsync(records, "owner-1", kept.Id, keptRecord, Breakdown("Kept"));

        var deleted = await vehicles.DeleteAsync("owner-1", doomed.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await records.GetAsync("owner-1", doomed.Id, doomedRecord, CancellationToken.None));
        Assert.Equal(1, await ServiceRecordTestData.CountAsync(dataSource, "service_records"));
        Assert.Equal(1, await ServiceRecordTestData.CountAsync(dataSource, "service_line_items"));
        Assert.Equal(2, await ServiceRecordTestData.CountAsync(dataSource, "service_line_item_parts"));
        Assert.Equal("Kept", Assert.Single((await records.GetAsync("owner-1", kept.Id, keptRecord, CancellationToken.None))!.LineItems).Description);
    }

    [Fact]
    public async Task NotFoundCases_ReturnNotFoundAndWriteNothing()
    {
        var (dataSource, vehicles, records) = await CreateAsync();
        var (vehicle, recordId) = await InsertRecordAsync(vehicles, records);
        var (otherVehicle, otherRecordId) = await InsertRecordAsync(vehicles, records);
        var lineId = Guid.CreateVersion7();
        await records.AddLineItemAsync("owner-1", vehicle.Id, recordId, lineId, Flat(), T0, CancellationToken.None);

        Assert.Equal(AddLineItemOutcome.NotFound, (await AddAsync(records, "owner-2", vehicle.Id, recordId, Flat())).Outcome);
        Assert.Equal(AddLineItemOutcome.NotFound, (await AddAsync(records, "owner-1", otherVehicle.Id, recordId, Flat())).Outcome);
        Assert.Equal(AddLineItemOutcome.NotFound, (await AddAsync(records, "owner-1", vehicle.Id, Guid.NewGuid(), Flat())).Outcome);

        Assert.Null(await records.UpdateLineItemAsync("owner-2", vehicle.Id, recordId, lineId, Flat("x", 1m), T0, CancellationToken.None));
        Assert.Null(await records.UpdateLineItemAsync("owner-1", otherVehicle.Id, recordId, lineId, Flat("x", 1m), T0, CancellationToken.None));
        Assert.Null(await records.UpdateLineItemAsync("owner-1", vehicle.Id, otherRecordId, lineId, Flat("x", 1m), T0, CancellationToken.None));
        Assert.Null(await records.UpdateLineItemAsync("owner-1", vehicle.Id, recordId, Guid.NewGuid(), Flat("x", 1m), T0, CancellationToken.None));

        Assert.False(await records.DeleteLineItemAsync("owner-2", vehicle.Id, recordId, lineId, CancellationToken.None));
        Assert.False(await records.DeleteLineItemAsync("owner-1", otherVehicle.Id, recordId, lineId, CancellationToken.None));
        Assert.False(await records.DeleteLineItemAsync("owner-1", vehicle.Id, otherRecordId, lineId, CancellationToken.None));
        Assert.False(await records.DeleteLineItemAsync("owner-1", vehicle.Id, recordId, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(1, await ServiceRecordTestData.CountAsync(dataSource, "service_line_items"));
        var unchanged = await records.GetAsync("owner-1", vehicle.Id, recordId, CancellationToken.None);
        Assert.Equal("Tire rotation", Assert.Single(unchanged!.LineItems).Description);
    }
}
