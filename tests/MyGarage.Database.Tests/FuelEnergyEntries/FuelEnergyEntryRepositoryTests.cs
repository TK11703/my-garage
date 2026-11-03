using MyGarage.Database.Common;
using MyGarage.Database.FuelEnergyEntries;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.FuelEnergyEntries;

[Collection(DatabaseCollection.Name)]
public class FuelEnergyEntryRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = TestData.Created;

    private async Task<(NpgsqlDataSource DataSource, VehicleRepository Vehicles, FuelEnergyEntryRepository Entries)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        return (dataSource, new VehicleRepository(dataSource, scripts), new FuelEnergyEntryRepository(dataSource, scripts));
    }

    private static async Task<VehicleRecord> InsertVehicleAsync(VehicleRepository vehicles, string ownerId)
    {
        var vehicle = TestData.Vehicle(ownerId);
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        return vehicle;
    }

    private static Task<FuelEnergyEntryRecord?> AddAsync(
        FuelEnergyEntryRepository entries, string ownerId, Guid vehicleId, DateOnly date, string kind = "fuel", DateTimeOffset? at = null) =>
        entries.AddAsync(ownerId, vehicleId, Guid.CreateVersion7(), kind, date, 3.459m, 12.345m, 42.70m, at ?? T0, CancellationToken.None);

    [Theory]
    [InlineData("fuel")]
    [InlineData("energy")]
    public async Task Add_StoresTheValuesWithAudit(string kind)
    {
        var (_, vehicles, entries) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        var entry = await entries.AddAsync("owner-1", vehicle.Id, Guid.CreateVersion7(), kind, new DateOnly(2026, 5, 1), 0.1342m, 28.5m, 3.82m, T0, CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal(vehicle.Id, entry.VehicleId);
        Assert.Equal(kind, entry.Kind);
        Assert.Equal(new DateOnly(2026, 5, 1), entry.EntryDate);
        Assert.Equal(0.1342m, entry.UnitPrice);
        Assert.Equal(28.5m, entry.Quantity);
        Assert.Equal(3.82m, entry.TotalCost);
        Assert.Equal(T0, entry.CreatedAt);
        Assert.Equal(T0, entry.UpdatedAt);
        Assert.Equal("owner-1", entry.CreatedBy);
        Assert.Equal("owner-1", entry.UpdatedBy);
    }

    [Fact]
    public async Task Add_AnotherOwnersOrUnknownVehicle_ReturnsNullAndInsertsNothing()
    {
        var (dataSource, vehicles, entries) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        Assert.Null(await AddAsync(entries, "owner-2", vehicle.Id, new DateOnly(2026, 5, 1)));
        Assert.Null(await AddAsync(entries, "owner-1", Guid.NewGuid(), new DateOnly(2026, 5, 1)));

        Assert.Equal(0, await MaintenanceTestData.CountAsync(dataSource, "fuel_energy_entries"));
    }

    [Fact]
    public async Task List_OrdersByEntryDateThenCreatedDescending_AndPages()
    {
        var (dataSource, vehicles, entries) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var older = await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id, entryDate: new DateOnly(2026, 1, 1), createdAt: T0);
        var newerFirst = await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id, entryDate: new DateOnly(2026, 3, 1), createdAt: T0);
        var newerSecond = await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id, entryDate: new DateOnly(2026, 3, 1), createdAt: T0.AddMinutes(1));

        var page1 = await entries.ListPageAsync("owner-1", vehicle.Id, 1, 2, CancellationToken.None);
        var page2 = await entries.ListPageAsync("owner-1", vehicle.Id, 2, 2, CancellationToken.None);
        var beyond = await entries.ListPageAsync("owner-1", vehicle.Id, 3, 2, CancellationToken.None);

        Assert.Equal([newerSecond, newerFirst], page1!.Items.Select(item => item.Id));
        Assert.Equal([older], page2!.Items.Select(item => item.Id));
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Empty(beyond!.Items);
    }

    [Fact]
    public async Task List_NeverMixesVehiclesOrOwners()
    {
        var (dataSource, vehicles, entries) = await CreateAsync();
        var vehicleA = await InsertVehicleAsync(vehicles, "owner-1");
        var vehicleB = await InsertVehicleAsync(vehicles, "owner-1");
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicleA.Id);

        var forB = await entries.ListPageAsync("owner-1", vehicleB.Id, 1, 10, CancellationToken.None);
        var otherOwner = await entries.ListPageAsync("owner-2", vehicleA.Id, 1, 10, CancellationToken.None);
        var unknown = await entries.ListPageAsync("owner-1", Guid.NewGuid(), 1, 10, CancellationToken.None);

        Assert.Empty(forB!.Items);
        Assert.Null(otherOwner);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task Get_ReturnsTheEntryOnlyThroughItsOwnVehicleAndOwner()
    {
        var (dataSource, vehicles, entries) = await CreateAsync();
        var vehicleA = await InsertVehicleAsync(vehicles, "owner-1");
        var vehicleB = await InsertVehicleAsync(vehicles, "owner-1");
        var id = await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicleA.Id);

        Assert.NotNull(await entries.GetAsync("owner-1", vehicleA.Id, id, CancellationToken.None));
        Assert.Null(await entries.GetAsync("owner-1", vehicleB.Id, id, CancellationToken.None));
        Assert.Null(await entries.GetAsync("owner-2", vehicleA.Id, id, CancellationToken.None));
        Assert.Null(await entries.GetAsync("owner-1", vehicleA.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Update_ReplacesTheValuesKeepsCreationAuditAndMovesTheEntryInTheList()
    {
        var (dataSource, vehicles, entries) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var first = await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id, entryDate: new DateOnly(2026, 1, 1), createdAt: T0);
        var second = await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id, entryDate: new DateOnly(2026, 2, 1), createdAt: T0);
        var later = T0.AddDays(1);

        var updated = await entries.UpdateAsync("owner-1", vehicle.Id, first, "energy", new DateOnly(2026, 6, 1), 0.2m, 10m, 2m, later, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("energy", updated.Kind);
        Assert.Equal(new DateOnly(2026, 6, 1), updated.EntryDate);
        Assert.Equal(0.2m, updated.UnitPrice);
        Assert.Equal(10m, updated.Quantity);
        Assert.Equal(2m, updated.TotalCost);
        Assert.Equal(T0, updated.CreatedAt);
        Assert.Equal("seed", updated.CreatedBy);
        Assert.Equal(later, updated.UpdatedAt);
        Assert.Equal("owner-1", updated.UpdatedBy);
        var list = await entries.ListPageAsync("owner-1", vehicle.Id, 1, 10, CancellationToken.None);
        Assert.Equal([first, second], list!.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task UpdateAndDelete_ChangeNothingThroughTheWrongVehicleOrOwner()
    {
        var (dataSource, vehicles, entries) = await CreateAsync();
        var vehicleA = await InsertVehicleAsync(vehicles, "owner-1");
        var vehicleB = await InsertVehicleAsync(vehicles, "owner-1");
        var id = await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicleA.Id, totalCost: 10m);

        Assert.Null(await entries.UpdateAsync("owner-1", vehicleB.Id, id, "fuel", new DateOnly(2026, 6, 1), 1m, 1m, 99m, T0, CancellationToken.None));
        Assert.Null(await entries.UpdateAsync("owner-2", vehicleA.Id, id, "fuel", new DateOnly(2026, 6, 1), 1m, 1m, 99m, T0, CancellationToken.None));
        Assert.Null(await entries.UpdateAsync("owner-1", vehicleA.Id, Guid.NewGuid(), "fuel", new DateOnly(2026, 6, 1), 1m, 1m, 99m, T0, CancellationToken.None));
        Assert.False(await entries.DeleteAsync("owner-1", vehicleB.Id, id, CancellationToken.None));
        Assert.False(await entries.DeleteAsync("owner-2", vehicleA.Id, id, CancellationToken.None));
        Assert.False(await entries.DeleteAsync("owner-1", vehicleA.Id, Guid.NewGuid(), CancellationToken.None));

        var stored = await entries.GetAsync("owner-1", vehicleA.Id, id, CancellationToken.None);
        Assert.Equal(10m, stored!.TotalCost);
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatEntry()
    {
        var (dataSource, vehicles, entries) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var keep = await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id);
        var remove = await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id);

        Assert.True(await entries.DeleteAsync("owner-1", vehicle.Id, remove, CancellationToken.None));

        Assert.Null(await entries.GetAsync("owner-1", vehicle.Id, remove, CancellationToken.None));
        Assert.NotNull(await entries.GetAsync("owner-1", vehicle.Id, keep, CancellationToken.None));
    }
}
