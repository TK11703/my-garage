using MyGarage.Database.Common;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Tests.ServiceRecords;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Vehicles;

[Collection(DatabaseCollection.Name)]
public class VehicleSpendRepositoryTests(PostgresFixture postgres)
{
    private const string Owner = "owner-a";
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    private async Task<(NpgsqlDataSource DataSource, VehicleRepository Repository)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (dataSource, new VehicleRepository(dataSource, new SqlScriptStore()));
    }

    private static async Task<VehicleRecord> AddVehicleAsync(VehicleRepository repository, string owner, string model)
    {
        var vehicle = TestData.Vehicle(owner, model);
        await repository.InsertAsync(vehicle, CancellationToken.None);
        return vehicle;
    }

    [Fact]
    public async Task Spend_SumsServiceRecordsFuelAndInspections_DatedInsideTheRange()
    {
        var (dataSource, repository) = await CreateAsync();
        var vehicle = await AddVehicleAsync(repository, Owner, "F-150");
        var record = await ServiceRecordTestData.InsertRecordAsync(dataSource, vehicle.Id, new DateOnly(2026, 3, 1));
        await ServiceRecordTestData.InsertLineItemAsync(dataSource, record, flatCost: 100m);
        await ServiceRecordTestData.InsertLineItemAsync(dataSource, record, flatCost: 20.50m);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id, entryDate: new DateOnly(2026, 4, 2), totalCost: 45m);
        await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicle.Id, inspectionDate: new DateOnly(2026, 5, 9), cost: 25m);

        var spend = await repository.GetSpendAsync(Owner, From, To, CancellationToken.None);

        Assert.Equal(190.50m, spend[vehicle.Id]);
    }

    [Fact]
    public async Task Spend_IncludesTheServiceRecordTax()
    {
        var (dataSource, repository) = await CreateAsync();
        var vehicle = await AddVehicleAsync(repository, Owner, "F-150");
        var record = await ServiceRecordTestData.InsertRecordAsync(dataSource, vehicle.Id, new DateOnly(2026, 3, 1));
        await ServiceRecordTestData.InsertLineItemAsync(dataSource, record, flatCost: 100m);
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await using var command = new NpgsqlCommand("UPDATE service_records SET tax_amount = 8.25 WHERE id = @id", connection);
            command.Parameters.AddWithValue("id", record);
            await command.ExecuteNonQueryAsync();
        }

        var spend = await repository.GetSpendAsync(Owner, From, To, CancellationToken.None);

        Assert.Equal(108.25m, spend[vehicle.Id]);
    }

    [Fact]
    public async Task Spend_LeavesOutEntriesOutsideTheRange_IncludingBothBoundaryDaysInside()
    {
        var (dataSource, repository) = await CreateAsync();
        var vehicle = await AddVehicleAsync(repository, Owner, "F-150");
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id, entryDate: new DateOnly(2025, 12, 31), totalCost: 1000m);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id, entryDate: new DateOnly(2026, 1, 1), totalCost: 10m);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id, entryDate: new DateOnly(2026, 12, 31), totalCost: 20m);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id, entryDate: new DateOnly(2027, 1, 1), totalCost: 1000m);

        var spend = await repository.GetSpendAsync(Owner, From, To, CancellationToken.None);

        Assert.Equal(30m, spend[vehicle.Id]);
    }

    [Fact]
    public async Task Spend_HasNoEntryForAVehicleWithNothingSpent()
    {
        var (dataSource, repository) = await CreateAsync();
        var spent = await AddVehicleAsync(repository, Owner, "F-150");
        var idle = await AddVehicleAsync(repository, Owner, "Ranger");
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, spent.Id, entryDate: new DateOnly(2026, 4, 2), totalCost: 45m);

        var spend = await repository.GetSpendAsync(Owner, From, To, CancellationToken.None);

        Assert.Equal([spent.Id], spend.Keys);
        Assert.False(spend.ContainsKey(idle.Id));
    }

    [Fact]
    public async Task Spend_IsScopedToTheOwner()
    {
        var (dataSource, repository) = await CreateAsync();
        var mine = await AddVehicleAsync(repository, Owner, "F-150");
        var theirs = await AddVehicleAsync(repository, "owner-b", "F-150");
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, mine.Id, entryDate: new DateOnly(2026, 4, 2), totalCost: 45m);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, theirs.Id, entryDate: new DateOnly(2026, 4, 2), totalCost: 999m);

        var spend = await repository.GetSpendAsync(Owner, From, To, CancellationToken.None);

        Assert.Equal([mine.Id], spend.Keys);
        Assert.Equal(45m, spend[mine.Id]);
    }

    [Fact]
    public async Task Spend_KeepsAnArchivedVehiclesMoney()
    {
        var (dataSource, repository) = await CreateAsync();
        var vehicle = await AddVehicleAsync(repository, Owner, "F-150");
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id, entryDate: new DateOnly(2026, 4, 2), totalCost: 45m);
        await repository.ArchiveAsync(Owner, vehicle.Id, new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);

        var spend = await repository.GetSpendAsync(Owner, From, To, CancellationToken.None);

        Assert.Equal(45m, spend[vehicle.Id]);
    }
}
