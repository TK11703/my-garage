using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Vehicles;

[Collection(DatabaseCollection.Name)]
public class VehicleRepositoryChangeTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Later = TestData.Created.AddDays(2);

    private async Task<(NpgsqlDataSource DataSource, VehicleRepository Repository)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (dataSource, new VehicleRepository(dataSource, new SqlScriptStore()));
    }

    [Fact]
    public async Task Update_ReplacesEveryEditableFieldAndStampsUpdate()
    {
        var (_, repository) = await CreateAsync();
        var original = TestData.Vehicle("owner-1", "Old", "Ford", "F-150", "XLT", "Blue", 100, 4);
        await repository.InsertAsync(original, CancellationToken.None);

        var changed = original with
        {
            Nickname = "New", Make = "Chevy", Model = "Silverado", Trim = "LT", Color = "Red", Mileage = 5_000, Doors = 2,
            UpdatedAt = Later, UpdatedBy = "owner-1", CreatedAt = Later, CreatedBy = "ignored",
        };
        var updated = await repository.UpdateAsync(changed, CancellationToken.None);
        var loaded = await repository.GetByIdAsync("owner-1", original.Id, CancellationToken.None);

        Assert.True(updated);
        Assert.NotNull(loaded);
        Assert.Equal("New", loaded.Nickname);
        Assert.Equal("Chevy", loaded.Make);
        Assert.Equal("Silverado", loaded.Model);
        Assert.Equal("LT", loaded.Trim);
        Assert.Equal("Red", loaded.Color);
        Assert.Equal(5_000, loaded.Mileage);
        Assert.Equal(2, loaded.Doors);
        Assert.Equal(Later, loaded.UpdatedAt);
        Assert.Equal(TestData.Created, loaded.CreatedAt);
        Assert.Equal("owner-1", loaded.CreatedBy);
    }

    [Fact]
    public async Task Update_CanClearOptionalFields()
    {
        var (_, repository) = await CreateAsync();
        var original = TestData.Vehicle("owner-1", trim: "XLT", color: "Blue", mileage: 100, doors: 4);
        await repository.InsertAsync(original, CancellationToken.None);

        await repository.UpdateAsync(original with { Trim = null, Color = null, Mileage = null, Doors = null }, CancellationToken.None);
        var loaded = await repository.GetByIdAsync("owner-1", original.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Null(loaded.Trim);
        Assert.Null(loaded.Color);
        Assert.Null(loaded.Mileage);
        Assert.Null(loaded.Doors);
    }

    [Fact]
    public async Task Update_AnotherOwnersVehicle_ReturnsFalseAndChangesNothing()
    {
        var (_, repository) = await CreateAsync();
        var original = TestData.Vehicle("owner-1", "Mine");
        await repository.InsertAsync(original, CancellationToken.None);

        var updated = await repository.UpdateAsync(original with { OwnerId = "owner-2", Nickname = "Hijacked" }, CancellationToken.None);

        Assert.False(updated);
        Assert.Equal("Mine", (await repository.GetByIdAsync("owner-1", original.Id, CancellationToken.None))!.Nickname);
    }

    [Fact]
    public async Task Update_UnknownVehicle_ReturnsFalse()
    {
        var (_, repository) = await CreateAsync();

        Assert.False(await repository.UpdateAsync(TestData.Vehicle("owner-1"), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesTheVehicleAndCascadesToChildren()
    {
        var (dataSource, repository) = await CreateAsync();
        var vehicle = TestData.Vehicle("owner-1");
        await repository.InsertAsync(vehicle, CancellationToken.None);
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            var vehicleId = vehicle.Id;
            await connection.ExecuteAsync(
                "INSERT INTO vehicle_tags (id, vehicle_id, name, created_at) VALUES (gen_random_uuid(), @vehicleId, 'a', now())", new { vehicleId });
            await connection.ExecuteAsync(
                "INSERT INTO journal_notes (id, vehicle_id, body, created_at, updated_at, created_by, updated_by) " +
                "VALUES (gen_random_uuid(), @vehicleId, 'b', now(), now(), 'o', 'o')", new { vehicleId });
            await connection.ExecuteAsync(
                "INSERT INTO vehicle_images (id, vehicle_id, content_type, size_bytes, created_at, created_by) " +
                "VALUES (gen_random_uuid(), @vehicleId, 'image/png', 10, now(), 'o')", new { vehicleId });
        }

        var deleted = await repository.DeleteAsync("owner-1", vehicle.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await repository.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None));
        await using var check = await dataSource.OpenConnectionAsync();
        Assert.Equal(0, await check.ExecuteScalarAsync<int>("SELECT count(*) FROM vehicle_tags"));
        Assert.Equal(0, await check.ExecuteScalarAsync<int>("SELECT count(*) FROM journal_notes"));
        Assert.Equal(0, await check.ExecuteScalarAsync<int>("SELECT count(*) FROM vehicle_images"));
    }

    [Fact]
    public async Task Delete_AnotherOwnersVehicle_ReturnsFalseAndKeepsIt()
    {
        var (_, repository) = await CreateAsync();
        var vehicle = TestData.Vehicle("owner-1");
        await repository.InsertAsync(vehicle, CancellationToken.None);

        Assert.False(await repository.DeleteAsync("owner-2", vehicle.Id, CancellationToken.None));
        Assert.NotNull(await repository.GetByIdAsync("owner-1", vehicle.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_UnknownVehicle_ReturnsFalse()
    {
        var (_, repository) = await CreateAsync();

        Assert.False(await repository.DeleteAsync("owner-1", Guid.NewGuid(), CancellationToken.None));
    }
}
