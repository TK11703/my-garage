using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.Tests.Common;

[Collection(DatabaseCollection.Name)]
public class MigrationRunnerTests(PostgresFixture postgres)
{
    private static MigrationRunner CreateRunner(NpgsqlDataSource dataSource) =>
        new(dataSource, new SqlScriptStore(), NullLogger<MigrationRunner>.Instance);

    [Fact]
    public async Task Run_EmptyDatabase_CreatesTablesAndLedger()
    {
        await using var dataSource = await postgres.CreateDatabaseAsync();

        await CreateRunner(dataSource).RunAsync();

        await using var connection = await dataSource.OpenConnectionAsync();
        var tables = (await connection.QueryAsync<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'")).ToHashSet();
        Assert.Superset(
            new HashSet<string> { "vehicles", "vehicle_tags", "journal_notes", "vehicle_images", "schema_migrations" },
            tables);

        var ledger = (await connection.QueryAsync<(string ScriptId, string Checksum, DateTime AppliedAt)>(
            "SELECT script_id AS ScriptId, checksum AS Checksum, applied_at AS AppliedAt FROM schema_migrations ORDER BY script_id")).ToList();
        Assert.Equal(4, ledger.Count);
        Assert.Equal("0001_create_vehicles", ledger[0].ScriptId);
        Assert.All(ledger, row => Assert.Equal(64, row.Checksum.Length));
    }

    [Fact]
    public async Task Run_Twice_AppliesNothingTheSecondTime()
    {
        await using var dataSource = await postgres.CreateDatabaseAsync();
        var runner = CreateRunner(dataSource);

        await runner.RunAsync();
        await runner.RunAsync();

        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal(4, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM schema_migrations"));
    }

    [Fact]
    public async Task Run_ChangedChecksum_Throws()
    {
        await using var dataSource = await postgres.CreateDatabaseAsync();
        var runner = CreateRunner(dataSource);
        await runner.RunAsync();

        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("UPDATE schema_migrations SET checksum = 'tampered' WHERE script_id = '0001_create_vehicles'");
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync());
        Assert.Contains("0001_create_vehicles", exception.Message);
    }

    [Fact]
    public async Task Run_ConcurrentRunners_ApplyEachScriptOnce()
    {
        await using var dataSource = await postgres.CreateDatabaseAsync();

        await Task.WhenAll(
            CreateRunner(dataSource).RunAsync(),
            CreateRunner(dataSource).RunAsync(),
            CreateRunner(dataSource).RunAsync());

        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.Equal(4, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM schema_migrations"));
    }

    [Theory]
    [InlineData("mileage", "-1")]
    [InlineData("mileage", "10000000")]
    [InlineData("doors", "0")]
    [InlineData("doors", "7")]
    public async Task Schema_VehicleChecks_RejectOutOfRangeValues(string column, string value)
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();

        var sql = $"INSERT INTO vehicles (id, owner_id, nickname, make, model, {column}, created_at, updated_at, created_by, updated_by) " +
                  $"VALUES (gen_random_uuid(), 'o', 'n', 'm', 'x', {value}, now(), now(), 'o', 'o')";

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(sql));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5242881)]
    public async Task Schema_ImageSizeCheck_RejectsOutOfRangeSizes(int size)
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var vehicleId = await InsertVehicleAsync(connection);

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "INSERT INTO vehicle_images (id, vehicle_id, content_type, size_bytes, created_at, created_by) " +
            "VALUES (gen_random_uuid(), @vehicleId, 'image/png', @size, now(), 'o')",
            new { vehicleId, size }));
    }

    [Fact]
    public async Task Schema_TagNames_AreUniquePerVehicleIgnoringCase()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var vehicleId = await InsertVehicleAsync(connection);
        const string insert = "INSERT INTO vehicle_tags (id, vehicle_id, name, created_at) VALUES (gen_random_uuid(), @vehicleId, @name, now())";

        await connection.ExecuteAsync(insert, new { vehicleId, name = "Sunroof" });

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(insert, new { vehicleId, name = "SUNROOF" }));
    }

    [Fact]
    public async Task Schema_OnlyOneMainImagePerVehicle()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var vehicleId = await InsertVehicleAsync(connection);
        const string insert = "INSERT INTO vehicle_images (id, vehicle_id, content_type, size_bytes, is_main, created_at, created_by) " +
                              "VALUES (gen_random_uuid(), @vehicleId, 'image/png', 10, @isMain, now(), 'o')";

        await connection.ExecuteAsync(insert, new { vehicleId, isMain = true });
        await connection.ExecuteAsync(insert, new { vehicleId, isMain = false });

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(insert, new { vehicleId, isMain = true }));
    }

    [Fact]
    public async Task Schema_DeletingVehicle_CascadesToChildren()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var vehicleId = await InsertVehicleAsync(connection);

        await connection.ExecuteAsync(
            "INSERT INTO vehicle_tags (id, vehicle_id, name, created_at) VALUES (gen_random_uuid(), @vehicleId, 'a', now())", new { vehicleId });
        await connection.ExecuteAsync(
            "INSERT INTO journal_notes (id, vehicle_id, body, created_at, updated_at, created_by, updated_by) " +
            "VALUES (gen_random_uuid(), @vehicleId, 'b', now(), now(), 'o', 'o')", new { vehicleId });
        await connection.ExecuteAsync(
            "INSERT INTO vehicle_images (id, vehicle_id, content_type, size_bytes, created_at, created_by) " +
            "VALUES (gen_random_uuid(), @vehicleId, 'image/png', 10, now(), 'o')", new { vehicleId });

        await connection.ExecuteAsync("DELETE FROM vehicles WHERE id = @vehicleId", new { vehicleId });

        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM vehicle_tags"));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM journal_notes"));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM vehicle_images"));
    }

    private static async Task<Guid> InsertVehicleAsync(NpgsqlConnection connection)
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO vehicles (id, owner_id, nickname, make, model, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, 'o', 'n', 'm', 'x', now(), now(), 'o', 'o')",
            new { id });
        return id;
    }
}
