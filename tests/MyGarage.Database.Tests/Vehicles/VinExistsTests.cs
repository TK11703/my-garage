using MyGarage.Database.Common;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;

namespace MyGarage.Database.Tests.Vehicles;

[Collection(DatabaseCollection.Name)]
public class VinExistsTests(PostgresFixture postgres)
{
    private const string Vin = "1FTFW1E50NFA00001";

    private async Task<VehicleRepository> CreateAsync() => new(await postgres.CreateMigratedDatabaseAsync(), new SqlScriptStore());

    [Fact]
    public async Task TheOwnersOtherVehicleWithTheSameVin_IsFound()
    {
        var vehicles = await CreateAsync();
        await vehicles.InsertAsync(TestData.Vehicle("owner-1") with { Vin = Vin }, CancellationToken.None);

        Assert.True(await vehicles.VinExistsAsync("owner-1", Vin, null, CancellationToken.None));
        Assert.True(await vehicles.VinExistsAsync("owner-1", Vin.ToLowerInvariant(), null, CancellationToken.None));
    }

    [Fact]
    public async Task TheEditedVehiclesOwnVin_IsNotADuplicate()
    {
        var vehicles = await CreateAsync();
        var vehicle = TestData.Vehicle("owner-1") with { Vin = Vin };
        await vehicles.InsertAsync(vehicle, CancellationToken.None);

        Assert.False(await vehicles.VinExistsAsync("owner-1", Vin, vehicle.Id, CancellationToken.None));
        Assert.True(await vehicles.VinExistsAsync("owner-1", Vin, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task AnotherOwnersVehicleWithTheSameVin_IsNeverReported()
    {
        var vehicles = await CreateAsync();
        await vehicles.InsertAsync(TestData.Vehicle("owner-2") with { Vin = Vin }, CancellationToken.None);

        Assert.False(await vehicles.VinExistsAsync("owner-1", Vin, null, CancellationToken.None));
    }

    [Fact]
    public async Task ADifferentVin_IsNotFound()
    {
        var vehicles = await CreateAsync();
        await vehicles.InsertAsync(TestData.Vehicle("owner-1") with { Vin = Vin }, CancellationToken.None);

        Assert.False(await vehicles.VinExistsAsync("owner-1", "1FTFW1E50NFA00002", null, CancellationToken.None));
    }
}
