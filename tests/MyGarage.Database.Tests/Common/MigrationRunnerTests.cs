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
            new HashSet<string> { "vehicles", "vehicle_tags", "features", "journal_notes", "vehicle_images", "lookup_items", "lookup_item_hidden", "schema_migrations" },
            tables);

        var ledger = (await connection.QueryAsync<(string ScriptId, string Checksum, DateTime AppliedAt)>(
            "SELECT script_id AS ScriptId, checksum AS Checksum, applied_at AS AppliedAt FROM schema_migrations ORDER BY script_id")).ToList();
        Assert.Equal(8, ledger.Count);
        Assert.Equal("0001_create_vehicles", ledger[0].ScriptId);
        Assert.Equal("0005_create_lookup_items", ledger[4].ScriptId);
        Assert.Equal("0006_add_vehicle_details", ledger[5].ScriptId);
        Assert.Equal("0007_drop_vehicle_nickname", ledger[6].ScriptId);
        Assert.Equal("0008_create_features", ledger[7].ScriptId);
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
        Assert.Equal(8, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM schema_migrations"));
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
        Assert.Equal(8, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM schema_migrations"));
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

        var sql = $"INSERT INTO vehicles (id, owner_id, make, model, {column}, created_at, updated_at, created_by, updated_by) " +
                  $"VALUES (gen_random_uuid(), 'o', 'm', 'x', {value}, now(), now(), 'o', 'o')";

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
    public async Task Schema_FeatureLinks_AreUniquePerVehicleIgnoringCase()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var vehicleId = await InsertVehicleAsync(connection);

        await TestData.LinkFeatureAsync(connection, vehicleId, "Sunroof");

        await Assert.ThrowsAsync<PostgresException>(() => TestData.LinkFeatureAsync(connection, vehicleId, "SUNROOF"));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM features"));
    }

    [Fact]
    public async Task Schema_FeatureNames_AreUniqueIgnoringCase()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        const string insert = "INSERT INTO features (id, name, created_at, created_by) VALUES (gen_random_uuid(), @name, now(), 'o')";

        await connection.ExecuteAsync(insert, new { name = "Sunroof" });

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(insert, new { name = "SUNROOF" }));
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

        await TestData.LinkFeatureAsync(connection, vehicleId, "a");
        await connection.ExecuteAsync(
            "INSERT INTO journal_notes (id, vehicle_id, body, created_at, updated_at, created_by, updated_by) " +
            "VALUES (gen_random_uuid(), @vehicleId, 'b', now(), now(), 'o', 'o')", new { vehicleId });
        await connection.ExecuteAsync(
            "INSERT INTO vehicle_images (id, vehicle_id, content_type, size_bytes, created_at, created_by) " +
            "VALUES (gen_random_uuid(), @vehicleId, 'image/png', 10, now(), 'o')", new { vehicleId });

        await connection.ExecuteAsync("DELETE FROM vehicles WHERE id = @vehicleId", new { vehicleId });

        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM vehicle_tags"));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM features"));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM journal_notes"));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM vehicle_images"));
    }

    [Fact]
    public async Task Seed_DefaultLookupItems_AreInDefinedOrder()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();

        var rows = (await connection.QueryAsync<(string Kind, string Name, int SortOrder, string? OwnerId, string? CreatedBy)>(
            "SELECT kind AS Kind, name AS Name, sort_order AS SortOrder, owner_id AS OwnerId, created_by AS CreatedBy FROM lookup_items ORDER BY kind, sort_order")).ToList();

        Assert.All(rows, row =>
        {
            Assert.Null(row.OwnerId);
            Assert.Null(row.CreatedBy);
        });

        static string[] Names(IEnumerable<(string Kind, string Name, int SortOrder, string? OwnerId, string? CreatedBy)> source, string kind) =>
            [.. source.Where(row => row.Kind == kind).Select(row => row.Name)];

        Assert.Equal(
            ["Sedan", "SUV", "Coupe", "Hatchback", "Convertible", "Truck", "Van", "Wagon", "Minivan", "Crossover", "Motorcycle"],
            Names(rows, "vehicle_type"));
        Assert.Equal(["Gasoline", "Diesel", "Electric", "Hybrid"], Names(rows, "fuel_type"));
        Assert.Equal(["Automatic", "Manual"], Names(rows, "transmission"));
        Assert.Equal(["FWD", "RWD", "AWD", "4WD"], Names(rows, "drivetrain"));
        Assert.Equal(21, rows.Count);
        Assert.Equal(Enumerable.Range(1, 11), rows.Where(row => row.Kind == "vehicle_type").Select(row => row.SortOrder));
    }

    [Fact]
    public async Task Schema_LookupItems_EnforceKindOwnerAndUniqueness()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        const string insert = "INSERT INTO lookup_items (id, kind, owner_id, name, created_at, created_by) VALUES (gen_random_uuid(), @kind, @ownerId, @name, now(), @createdBy)";

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(insert, new { kind = "body_style", ownerId = "a", name = "x", createdBy = "a" }));
        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(insert, new { kind = "fuel_type", ownerId = (string?)null, name = "gasoline", createdBy = (string?)null }));
        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(insert, new { kind = "fuel_type", ownerId = "a", name = "x", createdBy = (string?)null }));

        await connection.ExecuteAsync(insert, new { kind = "fuel_type", ownerId = "a", name = "Plug-in Hybrid", createdBy = "a" });
        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(insert, new { kind = "fuel_type", ownerId = "a", name = "PLUG-IN HYBRID", createdBy = "a" }));
        await connection.ExecuteAsync(insert, new { kind = "fuel_type", ownerId = "b", name = "Plug-in Hybrid", createdBy = "b" });
    }

    [Fact]
    public async Task Schema_DeletingLookupItem_CascadesHiddenRows()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var itemId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO lookup_items (id, kind, owner_id, name, created_at, created_by) VALUES (@itemId, 'drivetrain', 'a', 'Rear', now(), 'a')", new { itemId });
        await connection.ExecuteAsync(
            "INSERT INTO lookup_item_hidden (owner_id, item_id, hidden_at) VALUES ('a', @itemId, now())", new { itemId });

        await connection.ExecuteAsync("DELETE FROM lookup_items WHERE id = @itemId", new { itemId });

        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM lookup_item_hidden"));
    }

    [Fact]
    public async Task Migrations_0005Through0008_KeepExistingVehicleRowsAndDropTheNickname()
    {
        await using var dataSource = await postgres.CreateDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var scripts = new SqlScriptStore();
        await ApplyAsync(connection, scripts, AllMigrations[..4]);

        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO vehicles (id, owner_id, nickname, make, model, trim, color, mileage, doors, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, 'o', 'Old', 'Ford', 'F-150', 'XL', 'Red', 1200, 4, now(), now(), 'o', 'o')", new { id });

        await ApplyAsync(connection, scripts, AllMigrations[4..]);

        var row = await connection.QuerySingleAsync(
            "SELECT make, model, trim, color, mileage, doors, vin, year, vehicle_type_id, ownership, purchased_on, lease_ends_on, engine, " +
            "horsepower, fuel_type_id, transmission_id, seats, drivetrain_id, wheel_size, tire_size FROM vehicles WHERE id = @id", new { id });
        var values = (IDictionary<string, object?>)row;
        Assert.Equal("Ford", values["make"]);
        Assert.Equal("F-150", values["model"]);
        Assert.Equal("XL", values["trim"]);
        Assert.Equal("Red", values["color"]);
        Assert.Equal(1200, values["mileage"]);
        Assert.Equal((short)4, values["doors"]);
        foreach (var column in new[] { "vin", "year", "vehicle_type_id", "ownership", "purchased_on", "lease_ends_on", "engine", "horsepower", "fuel_type_id", "transmission_id", "seats", "drivetrain_id", "wheel_size", "tire_size" })
        {
            Assert.Null(values[column]);
        }

        Assert.DoesNotContain("nickname", await ColumnsAsync(connection, "vehicles"));
    }

    [Fact]
    public async Task Migration0007_DropsTheNicknameColumnAndItsIndex_AndAddsTheNameIndex()
    {
        await using var dataSource = await postgres.CreateDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var scripts = new SqlScriptStore();
        await ApplyAsync(connection, scripts, AllMigrations[..6]);
        Assert.Contains("nickname", await ColumnsAsync(connection, "vehicles"));
        Assert.Contains("ix_vehicles_owner_nickname", await IndexesAsync(connection, "vehicles"));

        await ApplyAsync(connection, scripts, AllMigrations[6]);

        Assert.DoesNotContain("nickname", await ColumnsAsync(connection, "vehicles"));
        var indexes = await IndexesAsync(connection, "vehicles");
        Assert.DoesNotContain("ix_vehicles_owner_nickname", indexes);
        Assert.Contains("ix_vehicles_owner_name", indexes);
    }

    [Fact]
    public async Task Migration0008_BackfillsOneSharedFeaturePerNameFromTheEarliestTag()
    {
        await using var dataSource = await postgres.CreateDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var scripts = new SqlScriptStore();
        await ApplyAsync(connection, scripts, AllMigrations[..7]);
        var vehicleA = await InsertVehicleAsync(connection, "owner-a");
        var vehicleB = await InsertVehicleAsync(connection, "owner-b");
        const string insertTag = "INSERT INTO vehicle_tags (id, vehicle_id, name, created_at) VALUES (gen_random_uuid(), @vehicleId, @name, @createdAt)";
        var earliest = TestData.Created;
        await connection.ExecuteAsync(insertTag, new { vehicleId = vehicleB, name = "sunroof", createdAt = earliest.AddHours(1) });
        await connection.ExecuteAsync(insertTag, new { vehicleId = vehicleA, name = "Sunroof", createdAt = earliest });
        await connection.ExecuteAsync(insertTag, new { vehicleId = vehicleB, name = "Tow hitch", createdAt = earliest.AddHours(2) });

        await ApplyAsync(connection, scripts, AllMigrations[7]);

        var features = (await connection.QueryAsync<(string Name, string CreatedBy, DateTime CreatedAt)>(
            "SELECT name AS Name, created_by AS CreatedBy, created_at AS CreatedAt FROM features ORDER BY name_key")).ToList();
        Assert.Equal(["Sunroof", "Tow hitch"], features.Select(feature => feature.Name));
        Assert.Equal(["owner-a", "owner-b"], features.Select(feature => feature.CreatedBy));
        Assert.Equal(earliest.UtcDateTime, features[0].CreatedAt.ToUniversalTime());

        Assert.Equal(3, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM vehicle_tags"));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM vehicle_tags WHERE feature_id IS NULL"));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT count(DISTINCT feature_id) FROM vehicle_tags"));
        var sunroofLinks = await connection.QueryAsync<string>(
            "SELECT f.name FROM vehicle_tags t JOIN features f ON f.id = t.feature_id WHERE t.vehicle_id IN (@vehicleA, @vehicleB) ORDER BY t.created_at",
            new { vehicleA, vehicleB });
        Assert.Equal(["Sunroof", "Sunroof", "Tow hitch"], sunroofLinks);

        var columns = await ColumnsAsync(connection, "vehicle_tags");
        Assert.DoesNotContain("name", columns);
        Assert.DoesNotContain("name_key", columns);
        Assert.Contains("feature_id", columns);
    }

    public static TheoryData<string, string> InvalidVehicleDetailValues => new()
    {
        { "vin", "'ABCDEFGHJKLMNPRS'" },
        { "vin", "'abcdefghjklmnprst'" },
        { "vin", "'1HGCM82633A00435I'" },
        { "year", "1899" },
        { "year", "2201" },
        { "ownership", "'rented'" },
        { "horsepower", "0" },
        { "horsepower", "3001" },
        { "seats", "0" },
        { "seats", "21" },
        { "wheel_size", "9.9" },
        { "wheel_size", "30.1" },
        { "lease_ends_on", "'2030-01-01'" },
    };

    [Theory]
    [MemberData(nameof(InvalidVehicleDetailValues))]
    public async Task Schema_VehicleDetailChecks_RejectInvalidValues(string column, string value)
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();

        var sql = $"INSERT INTO vehicles (id, owner_id, make, model, {column}, created_at, updated_at, created_by, updated_by) " +
                  $"VALUES (gen_random_uuid(), 'o', 'm', 'x', {value}, now(), now(), 'o', 'o')";

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(sql));
    }

    [Fact]
    public async Task Schema_LeaseRules_RequireLeasedAndOrderedDates()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        const string insert = "INSERT INTO vehicles (id, owner_id, make, model, ownership, purchased_on, lease_ends_on, created_at, updated_at, created_by, updated_by) " +
                              "VALUES (gen_random_uuid(), 'o', 'm', 'x', @ownership, @purchasedOn, @leaseEndsOn, now(), now(), 'o', 'o')";

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(insert, new { ownership = "purchased", purchasedOn = (DateTime?)null, leaseEndsOn = new DateTime(2030, 1, 1) }));
        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(insert, new { ownership = "leased", purchasedOn = new DateTime(2026, 5, 1), leaseEndsOn = new DateTime(2026, 4, 30) }));
        await connection.ExecuteAsync(insert, new { ownership = "leased", purchasedOn = new DateTime(2026, 5, 1), leaseEndsOn = new DateTime(2029, 5, 1) });
    }

    [Fact]
    public async Task Schema_VehicleLookupReferences_MustExist()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "INSERT INTO vehicles (id, owner_id, make, model, vehicle_type_id, created_at, updated_at, created_by, updated_by) " +
            "VALUES (gen_random_uuid(), 'o', 'm', 'x', gen_random_uuid(), now(), now(), 'o', 'o')"));
    }

    private static readonly string[] AllMigrations =
    [
        "0001_create_vehicles", "0002_create_vehicle_tags", "0003_create_journal_notes", "0004_create_vehicle_images",
        "0005_create_lookup_items", "0006_add_vehicle_details", "0007_drop_vehicle_nickname", "0008_create_features",
    ];

    private static async Task ApplyAsync(NpgsqlConnection connection, SqlScriptStore scripts, params string[] migrations)
    {
        foreach (var migration in migrations)
        {
            await connection.ExecuteAsync(scripts.Get($"Migrations/{migration}"));
        }
    }

    private static async Task<HashSet<string>> ColumnsAsync(NpgsqlConnection connection, string table) =>
        (await connection.QueryAsync<string>(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @table", new { table })).ToHashSet();

    private static async Task<HashSet<string>> IndexesAsync(NpgsqlConnection connection, string table) =>
        (await connection.QueryAsync<string>(
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'public' AND tablename = @table", new { table })).ToHashSet();

    private static async Task<Guid> InsertVehicleAsync(NpgsqlConnection connection, string ownerId = "o")
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO vehicles (id, owner_id, make, model, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @ownerId, 'm', 'x', now(), now(), @ownerId, @ownerId)",
            new { id, ownerId });
        return id;
    }
}
