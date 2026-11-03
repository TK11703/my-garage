using MyGarage.Database.Common;
using MyGarage.Database.Vehicles;

namespace MyGarage.Database.Tests.Common;

[Collection(DatabaseCollection.Name)]
public class MaintenanceCascadeTests(PostgresFixture postgres)
{
    private static readonly string[] Tables = ["fuel_energy_entries", "inspections", "registrations", "insurance_policies", "taxes"];

    [Fact]
    public async Task DeletingAVehicle_RemovesItsEntriesOfEveryKindAndLeavesOtherVehiclesAlone()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var vehicles = new VehicleRepository(dataSource, new SqlScriptStore());
        var doomed = TestData.Vehicle("owner-1");
        var survivor = TestData.Vehicle("owner-1");
        await vehicles.InsertAsync(doomed, CancellationToken.None);
        await vehicles.InsertAsync(survivor, CancellationToken.None);
        foreach (var vehicleId in new[] { doomed.Id, survivor.Id })
        {
            await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicleId);
            await MaintenanceTestData.InsertInspectionAsync(dataSource, vehicleId);
            await MaintenanceTestData.InsertRegistrationAsync(dataSource, vehicleId);
            await MaintenanceTestData.InsertInsurancePolicyAsync(dataSource, vehicleId);
            await MaintenanceTestData.InsertTaxAsync(dataSource, vehicleId);
        }

        Assert.True(await vehicles.DeleteAsync("owner-1", doomed.Id, CancellationToken.None));

        foreach (var table in Tables)
        {
            Assert.Equal(1, await MaintenanceTestData.CountAsync(dataSource, table));
        }
    }

    [Fact]
    public async Task DeletingAnotherOwnersVehicle_RemovesNothing()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var vehicles = new VehicleRepository(dataSource, new SqlScriptStore());
        var vehicle = TestData.Vehicle("owner-1");
        await vehicles.InsertAsync(vehicle, CancellationToken.None);
        await MaintenanceTestData.InsertFuelEnergyEntryAsync(dataSource, vehicle.Id);
        await MaintenanceTestData.InsertTaxAsync(dataSource, vehicle.Id);

        Assert.False(await vehicles.DeleteAsync("owner-2", vehicle.Id, CancellationToken.None));

        Assert.Equal(1, await MaintenanceTestData.CountAsync(dataSource, "fuel_energy_entries"));
        Assert.Equal(1, await MaintenanceTestData.CountAsync(dataSource, "taxes"));
    }
}
