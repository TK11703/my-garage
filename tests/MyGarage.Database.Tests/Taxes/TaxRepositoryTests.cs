using MyGarage.Database.Common;
using MyGarage.Database.Taxes;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Taxes;

[Collection(DatabaseCollection.Name)]
public class TaxRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = TestData.Created;

    private async Task<(NpgsqlDataSource DataSource, VehicleRepository Vehicles, TaxRepository Taxes)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var scripts = new SqlScriptStore();
        return (dataSource, new VehicleRepository(dataSource, scripts), new TaxRepository(dataSource, scripts));
    }

    private static async Task<VehicleRecord> InsertVehicleAsync(VehicleRepository vehicles, string ownerId)
    {
        var vehicle = TestData.Vehicle(ownerId);
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        return vehicle;
    }

    private static Task<TaxRecord?> AddAsync(
        TaxRepository taxes, string ownerId, Guid vehicleId, string type = "personal_property", string recurrence = "annual", DateTimeOffset? at = null) =>
        taxes.AddAsync(ownerId, vehicleId, Guid.CreateVersion7(), type, 312.40m, recurrence, at ?? T0, CancellationToken.None);

    [Theory]
    [InlineData("personal_property", "once", "0.01")]
    [InlineData("lease", "monthly", "312.40")]
    [InlineData("purchase", "bi_annual", "640.50")]
    [InlineData("personal_property", "annual", "9999999.99")]
    public async Task Add_StoresTheValuesWithAudit(string type, string recurrence, string amount)
    {
        var (_, vehicles, taxes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var amountValue = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);

        var tax = await taxes.AddAsync("owner-1", vehicle.Id, Guid.CreateVersion7(), type, amountValue, recurrence, T0, CancellationToken.None);

        Assert.NotNull(tax);
        Assert.Equal(vehicle.Id, tax.VehicleId);
        Assert.Equal(type, tax.TaxType);
        Assert.Equal(amountValue, tax.Amount);
        Assert.Equal(recurrence, tax.Recurrence);
        Assert.Equal(T0, tax.CreatedAt);
        Assert.Equal(T0, tax.UpdatedAt);
        Assert.Equal("owner-1", tax.CreatedBy);
        Assert.Equal("owner-1", tax.UpdatedBy);
    }

    [Fact]
    public async Task Add_AnotherOwnersOrUnknownVehicle_ReturnsNullAndInsertsNothing()
    {
        var (dataSource, vehicles, taxes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");

        Assert.Null(await AddAsync(taxes, "owner-2", vehicle.Id));
        Assert.Null(await AddAsync(taxes, "owner-1", Guid.NewGuid()));

        Assert.Equal(0, await MaintenanceTestData.CountAsync(dataSource, "taxes"));
    }

    [Fact]
    public async Task List_OrdersByWhenAddedDescending_AndPages()
    {
        var (dataSource, vehicles, taxes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var oldest = await MaintenanceTestData.InsertTaxAsync(dataSource, vehicle.Id, createdAt: T0);
        var tieFirst = await MaintenanceTestData.InsertTaxAsync(dataSource, vehicle.Id, createdAt: T0.AddMinutes(1));
        var tieSecond = await MaintenanceTestData.InsertTaxAsync(dataSource, vehicle.Id, createdAt: T0.AddMinutes(1));

        var page1 = await taxes.ListPageAsync("owner-1", vehicle.Id, 1, 2, CancellationToken.None);
        var page2 = await taxes.ListPageAsync("owner-1", vehicle.Id, 2, 2, CancellationToken.None);
        var beyond = await taxes.ListPageAsync("owner-1", vehicle.Id, 3, 2, CancellationToken.None);

        Assert.Equal([tieSecond, tieFirst], page1!.Items.Select(item => item.Id));
        Assert.Equal([oldest], page2!.Items.Select(item => item.Id));
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
        Assert.Empty(beyond!.Items);
    }

    [Fact]
    public async Task List_NeverMixesVehiclesOrOwners()
    {
        var (dataSource, vehicles, taxes) = await CreateAsync();
        var vehicleA = await InsertVehicleAsync(vehicles, "owner-1");
        var vehicleB = await InsertVehicleAsync(vehicles, "owner-1");
        await MaintenanceTestData.InsertTaxAsync(dataSource, vehicleA.Id);

        var forB = await taxes.ListPageAsync("owner-1", vehicleB.Id, 1, 10, CancellationToken.None);
        var otherOwner = await taxes.ListPageAsync("owner-2", vehicleA.Id, 1, 10, CancellationToken.None);
        var unknown = await taxes.ListPageAsync("owner-1", Guid.NewGuid(), 1, 10, CancellationToken.None);

        Assert.Empty(forB!.Items);
        Assert.Null(otherOwner);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task Get_ReturnsTheTaxOnlyThroughItsOwnVehicleAndOwner()
    {
        var (dataSource, vehicles, taxes) = await CreateAsync();
        var vehicleA = await InsertVehicleAsync(vehicles, "owner-1");
        var vehicleB = await InsertVehicleAsync(vehicles, "owner-1");
        var id = await MaintenanceTestData.InsertTaxAsync(dataSource, vehicleA.Id);

        var found = await taxes.GetAsync("owner-1", vehicleA.Id, id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("personal_property", found.TaxType);
        Assert.Equal("annual", found.Recurrence);
        Assert.Null(await taxes.GetAsync("owner-1", vehicleB.Id, id, CancellationToken.None));
        Assert.Null(await taxes.GetAsync("owner-2", vehicleA.Id, id, CancellationToken.None));
        Assert.Null(await taxes.GetAsync("owner-1", vehicleA.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Update_ReplacesTheValuesKeepsCreationAuditAndLeavesTheListOrder()
    {
        var (dataSource, vehicles, taxes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var first = await MaintenanceTestData.InsertTaxAsync(dataSource, vehicle.Id, createdAt: T0);
        var second = await MaintenanceTestData.InsertTaxAsync(dataSource, vehicle.Id, createdAt: T0.AddMinutes(1));
        var later = T0.AddDays(1);

        var updated = await taxes.UpdateAsync("owner-1", vehicle.Id, first, "lease", 40m, "monthly", later, CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal("lease", updated.TaxType);
        Assert.Equal(40m, updated.Amount);
        Assert.Equal("monthly", updated.Recurrence);
        Assert.Equal(T0, updated.CreatedAt);
        Assert.Equal("seed", updated.CreatedBy);
        Assert.Equal(later, updated.UpdatedAt);
        Assert.Equal("owner-1", updated.UpdatedBy);
        var list = await taxes.ListPageAsync("owner-1", vehicle.Id, 1, 10, CancellationToken.None);
        Assert.Equal([second, first], list!.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task UpdateAndDelete_ChangeNothingThroughTheWrongVehicleOrOwner()
    {
        var (dataSource, vehicles, taxes) = await CreateAsync();
        var vehicleA = await InsertVehicleAsync(vehicles, "owner-1");
        var vehicleB = await InsertVehicleAsync(vehicles, "owner-1");
        var id = await MaintenanceTestData.InsertTaxAsync(dataSource, vehicleA.Id, amount: 10m);

        Assert.Null(await taxes.UpdateAsync("owner-1", vehicleB.Id, id, "lease", 99m, "monthly", T0, CancellationToken.None));
        Assert.Null(await taxes.UpdateAsync("owner-2", vehicleA.Id, id, "lease", 99m, "monthly", T0, CancellationToken.None));
        Assert.Null(await taxes.UpdateAsync("owner-1", vehicleA.Id, Guid.NewGuid(), "lease", 99m, "monthly", T0, CancellationToken.None));
        Assert.False(await taxes.DeleteAsync("owner-1", vehicleB.Id, id, CancellationToken.None));
        Assert.False(await taxes.DeleteAsync("owner-2", vehicleA.Id, id, CancellationToken.None));
        Assert.False(await taxes.DeleteAsync("owner-1", vehicleA.Id, Guid.NewGuid(), CancellationToken.None));

        var stored = await taxes.GetAsync("owner-1", vehicleA.Id, id, CancellationToken.None);
        Assert.Equal(10m, stored!.Amount);
        Assert.Equal("personal_property", stored.TaxType);
        Assert.Equal("annual", stored.Recurrence);
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatTax()
    {
        var (dataSource, vehicles, taxes) = await CreateAsync();
        var vehicle = await InsertVehicleAsync(vehicles, "owner-1");
        var keep = await MaintenanceTestData.InsertTaxAsync(dataSource, vehicle.Id);
        var remove = await MaintenanceTestData.InsertTaxAsync(dataSource, vehicle.Id);

        Assert.True(await taxes.DeleteAsync("owner-1", vehicle.Id, remove, CancellationToken.None));

        Assert.Null(await taxes.GetAsync("owner-1", vehicle.Id, remove, CancellationToken.None));
        Assert.NotNull(await taxes.GetAsync("owner-1", vehicle.Id, keep, CancellationToken.None));
    }
}
