using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;

namespace MyGarage.Database.Tests.Vehicles;

[Collection(DatabaseCollection.Name)]
public class VehicleArchivedStateTests(PostgresFixture postgres)
{
    [Fact]
    public async Task GetArchivedState_IsNullForUnknownAndForeignVehicles_FalseWhenActive_TrueWhenArchived()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var repository = new VehicleRepository(dataSource, new SqlScriptStore());
        var vehicle = TestData.Vehicle("owner-a", "Outback", "Subaru", "Limited", "Blue", 42_000, 4);
        await repository.InsertAsync(vehicle, CancellationToken.None);

        Assert.Null(await repository.GetArchivedStateAsync("owner-a", Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await repository.GetArchivedStateAsync("owner-b", vehicle.Id, CancellationToken.None));
        Assert.False(await repository.GetArchivedStateAsync("owner-a", vehicle.Id, CancellationToken.None));

        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("UPDATE vehicles SET archived_at = now() WHERE id = @Id", new { vehicle.Id });
        }

        Assert.True(await repository.GetArchivedStateAsync("owner-a", vehicle.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GetById_CarriesArchivedAt_NullWhenActive_AndTheStoredInstantWhenArchived()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var repository = new VehicleRepository(dataSource, new SqlScriptStore());
        var vehicle = TestData.Vehicle("owner-a", "Outback", "Subaru", "Limited", "Blue", 42_000, 4);
        await repository.InsertAsync(vehicle, CancellationToken.None);
        Assert.Null((await repository.GetByIdAsync("owner-a", vehicle.Id, CancellationToken.None))!.ArchivedAt);

        var archivedAt = new DateTimeOffset(2026, 11, 8, 10, 30, 0, TimeSpan.Zero);
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("UPDATE vehicles SET archived_at = @archivedAt WHERE id = @Id", new { archivedAt, vehicle.Id });
        }

        Assert.Equal(archivedAt, (await repository.GetByIdAsync("owner-a", vehicle.Id, CancellationToken.None))!.ArchivedAt);
    }
}
