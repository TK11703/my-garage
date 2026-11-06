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
            new HashSet<string> { "vehicles", "vehicle_tags", "features", "journal_notes", "vehicle_images", "lookup_items", "lookup_item_hidden", "service_records", "service_line_items", "service_line_item_parts", "fuel_energy_entries", "inspections", "registrations", "insurance_policies", "taxes", "user_profiles", "email_submissions", "email_attachments", "maintenance_drafts", "notifications", "vehicle_documents", "schema_migrations" },
            tables);

        var ledger = (await connection.QueryAsync<(string ScriptId, string Checksum, DateTime AppliedAt)>(
            "SELECT script_id AS ScriptId, checksum AS Checksum, applied_at AS AppliedAt FROM schema_migrations ORDER BY script_id")).ToList();
        Assert.Equal(16, ledger.Count);
        Assert.Equal("0009_create_service_records", ledger[8].ScriptId);
        Assert.Equal("0010_add_vehicle_latest_odometer", ledger[9].ScriptId);
        Assert.Equal("0011_create_maintenance_records", ledger[10].ScriptId);
        Assert.Equal("0012_create_email_intake", ledger[11].ScriptId);
        Assert.Equal("0013_pricing_discounts_labor_tax", ledger[12].ScriptId);
        Assert.Equal("0014_add_feature_category", ledger[13].ScriptId);
        Assert.Equal("0015_add_upload_origin", ledger[14].ScriptId);
        Assert.Equal("0016_create_reminders", ledger[15].ScriptId);
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
        Assert.Equal(16, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM schema_migrations"));
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
        Assert.Equal(16, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM schema_migrations"));
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
    public async Task Migration0010_AddsTheLatestOdometerColumn_AndLeavesExistingVehiclesUnchanged()
    {
        await using var dataSource = await postgres.CreateDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var scripts = new SqlScriptStore();
        await ApplyAsync(connection, scripts, AllMigrations[..9]);
        var id = await InsertVehicleAsync(connection);
        await connection.ExecuteAsync("UPDATE vehicles SET mileage = 1200 WHERE id = @id", new { id });

        await ApplyAsync(connection, scripts, AllMigrations[9]);

        var row = await connection.QuerySingleAsync<(int? Mileage, int? Latest)>(
            "SELECT mileage AS Mileage, latest_odometer_reading AS Latest FROM vehicles WHERE id = @id", new { id });
        Assert.Equal(1200, row.Mileage);
        Assert.Null(row.Latest);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10000000)]
    public async Task Schema_LatestOdometerCheck_RejectsOutOfRangeValues(int value)
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "INSERT INTO vehicles (id, owner_id, make, model, latest_odometer_reading, created_at, updated_at, created_by, updated_by) " +
            "VALUES (gen_random_uuid(), 'o', 'm', 'x', @value, now(), now(), 'o', 'o')", new { value }));
    }

    public static TheoryData<string, string> InvalidServiceRecordValues => new()
    {
        { "odometer_reading", "-1" },
        { "odometer_reading", "10000000" },
        { "date_serviced", "'1899-12-31'" },
        { "shop_or_person", "repeat('x', 101)" },
    };

    [Theory]
    [MemberData(nameof(InvalidServiceRecordValues))]
    public async Task Schema_ServiceRecordChecks_RejectInvalidValues(string column, string value)
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var vehicleId = await InsertVehicleAsync(connection);
        var values = new Dictionary<string, string> { ["date_serviced"] = "'2026-05-01'", ["odometer_reading"] = "100", ["shop_or_person"] = "null" };
        values[column] = value;

        var sql = "INSERT INTO service_records (id, vehicle_id, date_serviced, odometer_reading, shop_or_person, created_at, updated_at, created_by, updated_by) " +
                  $"VALUES (gen_random_uuid(), @vehicleId, {values["date_serviced"]}, {values["odometer_reading"]}, {values["shop_or_person"]}, now(), now(), 'o', 'o')";

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(sql, new { vehicleId }));
    }

    public static TheoryData<string> InvalidLineItemAssignments => new()
    {
        "flat_cost = -10000000.00",
        "flat_cost = 10000000.00",
        "labor_hours = 0, labor_rate = 1",
        "labor_hours = 1000, labor_rate = 1",
        "labor_hours = 1, labor_rate = -0.01",
        "labor_hours = 1, labor_rate = 100000.00",
        "total_cost = -10000000.00",
        "total_cost = 10000000.00",
        "labor_hours = 1",
        "labor_rate = 1",
        "flat_cost = 5, labor_hours = 1, labor_rate = 1",
        "labor_cost = -0.01",
        "labor_cost = 10000000.00",
        "labor_cost = 5, labor_hours = 1, labor_rate = 1",
        "flat_cost = 5, labor_cost = 5",
    };

    [Theory]
    [MemberData(nameof(InvalidLineItemAssignments))]
    public async Task Schema_ServiceLineItemChecks_RejectInvalidValues(string assignments)
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var recordId = await InsertServiceRecordAsync(connection, await InsertVehicleAsync(connection));
        var lineItemId = await InsertLineItemAsync(connection, recordId);

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            $"UPDATE service_line_items SET {assignments} WHERE id = @lineItemId", new { lineItemId }));
    }

    [Fact]
    public async Task Migration0014_LeavesExistingFeaturesWithoutACategory_AndListsThemAsPending()
    {
        await using var dataSource = await postgres.CreateDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var scripts = new SqlScriptStore();
        await ApplyAsync(connection, scripts, AllMigrations[..13]);
        await connection.ExecuteAsync("INSERT INTO features (id, name, created_at, created_by) VALUES (gen_random_uuid(), 'Sunroof', now(), 'o')");

        await ApplyAsync(connection, scripts, AllMigrations[13]);

        Assert.Null(await connection.ExecuteScalarAsync<string?>("SELECT category FROM features WHERE name = 'Sunroof'"));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM features WHERE category IS NULL"));
    }

    [Fact]
    public async Task Migration0013_AllowsDiscountsAndEnteredLaborCost_AndAddsRecordTax()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var recordId = await InsertServiceRecordAsync(connection, await InsertVehicleAsync(connection));
        var lineItemId = await InsertLineItemAsync(connection, recordId);

        await connection.ExecuteAsync("UPDATE service_line_items SET flat_cost = -4.04, total_cost = -4.04 WHERE id = @lineItemId", new { lineItemId });
        await connection.ExecuteAsync("UPDATE service_line_items SET flat_cost = -9999999.99, total_cost = -9999999.99 WHERE id = @lineItemId", new { lineItemId });
        await connection.ExecuteAsync("UPDATE service_line_items SET flat_cost = NULL, labor_cost = 180.00, total_cost = 180.00 WHERE id = @lineItemId", new { lineItemId });
        await connection.ExecuteAsync("UPDATE service_records SET tax_amount = 8.25 WHERE id = @recordId", new { recordId });

        Assert.Equal(180.00m, await connection.ExecuteScalarAsync<decimal>("SELECT labor_cost FROM service_line_items WHERE id = @lineItemId", new { lineItemId }));
        Assert.Equal(8.25m, await connection.ExecuteScalarAsync<decimal>("SELECT tax_amount FROM service_records WHERE id = @recordId", new { recordId }));
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("10000000.00")]
    public async Task Migration0013_RejectsTaxOutsideItsRange(string tax)
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var recordId = await InsertServiceRecordAsync(connection, await InsertVehicleAsync(connection));

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync($"UPDATE service_records SET tax_amount = {tax} WHERE id = @recordId", new { recordId }));
    }

    [Fact]
    public async Task Migration0013_KeepsExistingRowsUnchanged_AndNewColumnsReadNull()
    {
        await using var dataSource = await postgres.CreateDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var scripts = new SqlScriptStore();
        await ApplyAsync(connection, scripts, AllMigrations[..12]);
        var recordId = await InsertServiceRecordAsync(connection, await InsertVehicleAsync(connection));
        var lineItemId = await InsertLineItemAsync(connection, recordId);
        await connection.ExecuteAsync("UPDATE service_line_items SET flat_cost = 12.34, total_cost = 12.34 WHERE id = @lineItemId", new { lineItemId });

        await ApplyAsync(connection, scripts, AllMigrations[12]);

        var line = await connection.QuerySingleAsync<(decimal Flat, decimal Total, decimal? LaborCost)>(
            "SELECT flat_cost AS Flat, total_cost AS Total, labor_cost AS LaborCost FROM service_line_items WHERE id = @lineItemId", new { lineItemId });
        Assert.Equal(12.34m, line.Flat);
        Assert.Equal(12.34m, line.Total);
        Assert.Null(line.LaborCost);
        Assert.Null(await connection.ExecuteScalarAsync<decimal?>("SELECT tax_amount FROM service_records WHERE id = @recordId", new { recordId }));
    }

    [Fact]
    public async Task Schema_ServiceLinePartChecks_RejectInvalidValuesAndDuplicatePositions()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var recordId = await InsertServiceRecordAsync(connection, await InsertVehicleAsync(connection));
        var lineItemId = await InsertLineItemAsync(connection, recordId);
        const string insert = "INSERT INTO service_line_item_parts (id, line_item_id, position, name, quantity, unit_price) " +
                              "VALUES (gen_random_uuid(), @lineItemId, @position, 'Filter', @quantity, @unitPrice)";

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(insert, new { lineItemId, position = 1, quantity = 0m, unitPrice = 1m }));
        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(insert, new { lineItemId, position = 1, quantity = 10000m, unitPrice = 1m }));
        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(insert, new { lineItemId, position = 1, quantity = 1m, unitPrice = -0.01m }));

        await connection.ExecuteAsync(insert, new { lineItemId, position = 1, quantity = 1m, unitPrice = 12.5m });
        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(insert, new { lineItemId, position = 1, quantity = 1m, unitPrice = 1m }));
    }

    [Fact]
    public async Task Schema_DeletingParents_CascadesThroughServiceRecordTables()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var vehicleId = await InsertVehicleAsync(connection);
        var recordId = await InsertServiceRecordAsync(connection, vehicleId);
        var lineItemId = await InsertLineItemAsync(connection, recordId);
        await connection.ExecuteAsync(
            "INSERT INTO service_line_item_parts (id, line_item_id, position, name, quantity, unit_price) VALUES (gen_random_uuid(), @lineItemId, 1, 'Filter', 1, 1)",
            new { lineItemId });

        await connection.ExecuteAsync("DELETE FROM service_line_items WHERE id = @lineItemId", new { lineItemId });
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM service_line_item_parts"));

        var secondLine = await InsertLineItemAsync(connection, recordId);
        await connection.ExecuteAsync(
            "INSERT INTO service_line_item_parts (id, line_item_id, position, name, quantity, unit_price) VALUES (gen_random_uuid(), @secondLine, 1, 'Filter', 1, 1)",
            new { secondLine });
        await connection.ExecuteAsync("DELETE FROM service_records WHERE id = @recordId", new { recordId });
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM service_line_items"));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM service_line_item_parts"));

        var thirdRecord = await InsertServiceRecordAsync(connection, vehicleId);
        var thirdLine = await InsertLineItemAsync(connection, thirdRecord);
        await connection.ExecuteAsync(
            "INSERT INTO service_line_item_parts (id, line_item_id, position, name, quantity, unit_price) VALUES (gen_random_uuid(), @thirdLine, 1, 'Filter', 1, 1)",
            new { thirdLine });
        await connection.ExecuteAsync("DELETE FROM vehicles WHERE id = @vehicleId", new { vehicleId });
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM service_records"));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM service_line_items"));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM service_line_item_parts"));
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

    private static readonly Dictionary<string, string[]> EmailIntakeColumns = new()
    {
        ["user_profiles"] = ["owner_id", "email", "display_name", "first_seen_at", "last_seen_at"],
        ["email_submissions"] =
        [
            "id", "message_id", "status", "arrived_at", "received_at", "owner_id", "sender", "subject", "body_text", "attempt_count",
            "next_attempt_at", "locked_until", "failure_reason", "skipped_attachments", "content_purged_at", "origin", "preselected_vehicle_id",
        ],
        ["email_attachments"] = ["id", "submission_id", "owner_id", "file_name", "content_type", "size_bytes", "sha256", "created_at"],
        ["maintenance_drafts"] =
        [
            "id", "owner_id", "submission_id", "attachment_id", "state", "kind", "vehicle_id", "vehicle_match", "confidence", "proposal",
            "notes", "created_record_kind", "created_record_id", "created_at", "expires_at", "decided_at",
        ],
        ["notifications"] = ["id", "owner_id", "kind", "draft_id", "title", "created_at", "read_at", "reminder_id"],
        ["vehicle_documents"] = ["id", "vehicle_id", "file_name", "content_type", "size_bytes", "source_kind", "source_record_id", "created_at", "created_by"],
    };

    private static readonly Dictionary<string, string[]> EmailIntakeIndexes = new()
    {
        ["user_profiles"] = ["ix_user_profiles_email"],
        ["email_submissions"] = ["ix_email_submissions_queue", "ix_email_submissions_owner"],
        ["email_attachments"] = ["ix_email_attachments_owner_hash"],
        ["maintenance_drafts"] = ["ix_maintenance_drafts_owner_open", "ix_maintenance_drafts_expiry", "ix_maintenance_drafts_submission"],
        ["notifications"] = ["ix_notifications_owner", "ix_notifications_unread"],
        ["vehicle_documents"] = ["ix_vehicle_documents_vehicle"],
    };

    private static readonly string[] RecordTables =
        ["service_records", "fuel_energy_entries", "inspections", "registrations", "insurance_policies", "taxes"];

    [Fact]
    public async Task Migration0012_CreatesTheEmailIntakeTablesWithTheirColumnsAndIndexes()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();

        foreach (var (table, columns) in EmailIntakeColumns)
        {
            Assert.Equal(columns.ToHashSet(), await ColumnsAsync(connection, table));
            Assert.Superset(EmailIntakeIndexes[table].ToHashSet(), await IndexesAsync(connection, table));
        }
    }

    [Fact]
    public async Task Migration0012_AddsFromEmailToTheSixRecordTables_AndExistingRowsReadFalse()
    {
        await using var dataSource = await postgres.CreateDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var scripts = new SqlScriptStore();
        await ApplyAsync(connection, scripts, AllMigrations[..11]);
        var vehicleId = await InsertVehicleAsync(connection);
        await InsertOneRowInEachRecordTableAsync(connection, vehicleId);

        await ApplyAsync(connection, scripts, AllMigrations[11]);

        foreach (var table in RecordTables)
        {
            var column = await connection.QuerySingleAsync<(string DataType, string IsNullable, string? Default)>(
                "SELECT data_type AS DataType, is_nullable AS IsNullable, column_default AS Default FROM information_schema.columns " +
                "WHERE table_schema = 'public' AND table_name = @table AND column_name = 'from_email'", new { table });
            Assert.Equal("boolean", column.DataType);
            Assert.Equal("NO", column.IsNullable);
            Assert.Equal("false", column.Default);
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table} WHERE from_email = false"));
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table}"));
        }

        // A row inserted by code that does not know the column also reads false.
        await InsertOneRowInEachRecordTableAsync(connection, vehicleId);
        foreach (var table in RecordTables)
        {
            Assert.Equal(2, await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table} WHERE from_email = false"));
        }
    }

    [Fact]
    public async Task Migration0012_ChangesNoOtherExistingTable()
    {
        await using var dataSource = await postgres.CreateDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var scripts = new SqlScriptStore();
        await ApplyAsync(connection, scripts, AllMigrations[..11]);
        var before = await ColumnDefinitionsAsync(connection);

        await ApplyAsync(connection, scripts, AllMigrations[11]);
        var after = await ColumnDefinitionsAsync(connection);

        var added = after.Except(before).Select(definition => definition.Split('|')).ToList();
        Assert.All(added, parts => Assert.True(
            EmailIntakeColumns.ContainsKey(parts[1]) || (parts[0] == "from_email" && RecordTables.Contains(parts[1])),
            $"Unexpected change to {parts[1]}.{parts[0]}"));
        Assert.Equal(RecordTables.Length, added.Count(parts => parts[0] == "from_email"));
        Assert.Empty(before.Except(after));
    }

    [Fact]
    public async Task Schema_EmailSubmissions_RejectDuplicateMessageIdsAndOutOfRangeValues()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();

        await InsertSubmissionAsync(connection, "message-1");

        await Assert.ThrowsAsync<PostgresException>(() => InsertSubmissionAsync(connection, "message-1"));
        await Assert.ThrowsAsync<PostgresException>(() => InsertSubmissionAsync(connection, new string('m', 513)));
        await Assert.ThrowsAsync<PostgresException>(() => InsertSubmissionAsync(connection, "message-2", status: "done"));
        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "INSERT INTO email_submissions (id, message_id, status, arrived_at, body_text, next_attempt_at) VALUES (gen_random_uuid(), 'big', 'queued', now(), repeat('x', 100001), now())"));

        await InsertSubmissionAsync(connection, new string('m', 512));
        foreach (var status in new[] { "queued", "processing", "ready", "not_applicable", "not_accepted", "failed" })
        {
            await InsertSubmissionAsync(connection, $"status-{status}", status);
        }
    }

    [Theory]
    [InlineData("text/plain", 10)]
    [InlineData("application/pdf", 0)]
    [InlineData("application/pdf", 10485761)]
    public async Task Schema_EmailAttachments_RejectUnsupportedTypesAndSizes(string contentType, int size)
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var submissionId = await InsertSubmissionAsync(connection, "m1");

        await Assert.ThrowsAsync<PostgresException>(() => InsertAttachmentAsync(connection, submissionId, contentType, size));
    }

    [Fact]
    public async Task Schema_EmailAttachments_AcceptTheBoundaries()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var submissionId = await InsertSubmissionAsync(connection, "m1");

        await InsertAttachmentAsync(connection, submissionId, "application/pdf", 1);
        await InsertAttachmentAsync(connection, submissionId, "image/webp", 10485760);
        await InsertAttachmentAsync(connection, submissionId, "image/jpeg", 100);
        await InsertAttachmentAsync(connection, submissionId, "image/png", 100);
    }

    [Theory]
    [InlineData("state", "'open'")]
    [InlineData("kind", "'oil_change'")]
    [InlineData("vehicle_match", "'guess'")]
    [InlineData("confidence", "1.01")]
    [InlineData("confidence", "-0.01")]
    public async Task Schema_MaintenanceDrafts_RejectInvalidValues(string column, string value)
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var submissionId = await InsertSubmissionAsync(connection, "m1");
        var values = new Dictionary<string, string> { ["state"] = "'needs_review'", ["kind"] = "'service_record'", ["vehicle_match"] = "'none'", ["confidence"] = "0.5" };
        values[column] = value;

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "INSERT INTO maintenance_drafts (id, owner_id, submission_id, state, kind, vehicle_match, confidence, created_at, expires_at) " +
            $"VALUES (gen_random_uuid(), 'o', @submissionId, {values["state"]}, {values["kind"]}, {values["vehicle_match"]}, {values["confidence"]}, now(), now())",
            new { submissionId }));
    }

    [Fact]
    public async Task Schema_MaintenanceDrafts_AcceptNullKindAndConfidenceAndEveryKindAndState()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var submissionId = await InsertSubmissionAsync(connection, "m1");

        await InsertDraftAsync(connection, submissionId, kind: null, state: "manual_entry", confidence: null);
        foreach (var kind in new[] { "service_record", "fuel", "energy", "inspection", "registration", "insurance", "tax" })
        {
            await InsertDraftAsync(connection, submissionId, kind: kind);
        }

        foreach (var state in new[] { "needs_review", "manual_entry", "included", "discarded", "expired" })
        {
            await InsertDraftAsync(connection, submissionId, state: state);
        }

        foreach (var match in new[] { "vin", "description", "only_vehicle", "none" })
        {
            await InsertDraftAsync(connection, submissionId, vehicleMatch: match);
        }

        await InsertDraftAsync(connection, submissionId, confidence: 0m);
        await InsertDraftAsync(connection, submissionId, confidence: 1m);
        Assert.Equal("{}", await connection.ExecuteScalarAsync<string>("SELECT proposal::text FROM maintenance_drafts LIMIT 1"));
    }

    [Fact]
    public async Task Schema_Notifications_RejectUnknownKinds()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "INSERT INTO notifications (id, owner_id, kind, title, created_at) VALUES (gen_random_uuid(), 'o', 'other', 't', now())"));
        await connection.ExecuteAsync(
            "INSERT INTO notifications (id, owner_id, kind, title, created_at) VALUES (gen_random_uuid(), 'o', 'draft_ready', 't', now())");
    }

    [Theory]
    [InlineData("text/plain", 10)]
    [InlineData("application/pdf", 0)]
    [InlineData("application/pdf", 10485761)]
    public async Task Schema_VehicleDocuments_RejectUnsupportedTypesAndSizes(string contentType, int size)
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var vehicleId = await InsertVehicleAsync(connection);

        await Assert.ThrowsAsync<PostgresException>(() => InsertDocumentAsync(connection, vehicleId, contentType, size));
    }

    [Fact]
    public async Task Schema_VehicleDocuments_AcceptTheBoundaries()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var vehicleId = await InsertVehicleAsync(connection);

        await InsertDocumentAsync(connection, vehicleId, "application/pdf", 1);
        await InsertDocumentAsync(connection, vehicleId, "image/png", 10485760);
    }

    [Fact]
    public async Task Schema_DeletingAVehicle_CascadesToDocumentsAndDetachesDrafts()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var vehicleId = await InsertVehicleAsync(connection);
        var submissionId = await InsertSubmissionAsync(connection, "m1");
        await InsertDocumentAsync(connection, vehicleId, "application/pdf", 10);
        var draftId = await InsertDraftAsync(connection, submissionId, vehicleId: vehicleId);

        await connection.ExecuteAsync("DELETE FROM vehicles WHERE id = @vehicleId", new { vehicleId });

        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM vehicle_documents"));
        Assert.Null(await connection.ExecuteScalarAsync<Guid?>("SELECT vehicle_id FROM maintenance_drafts WHERE id = @draftId", new { draftId }));
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM maintenance_drafts"));
    }

    [Fact]
    public async Task Schema_DeletingASubmission_CascadesToAttachmentsAndDrafts_AndADraftToItsNotifications()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var submissionId = await InsertSubmissionAsync(connection, "m1");
        var attachmentId = await InsertAttachmentAsync(connection, submissionId, "application/pdf", 10);
        var draftId = await InsertDraftAsync(connection, submissionId, attachmentId: attachmentId);
        await InsertNotificationAsync(connection, draftId);

        await connection.ExecuteAsync("DELETE FROM email_submissions WHERE id = @submissionId", new { submissionId });

        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM email_attachments"));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM maintenance_drafts"));
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM notifications"));
    }

    [Fact]
    public async Task Schema_DeletingADraft_CascadesToItsNotifications_AndAnAttachmentDetachesItsDraft()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var submissionId = await InsertSubmissionAsync(connection, "m1");
        var attachmentId = await InsertAttachmentAsync(connection, submissionId, "image/png", 10);
        var firstDraft = await InsertDraftAsync(connection, submissionId);
        var secondDraft = await InsertDraftAsync(connection, submissionId, attachmentId: attachmentId);
        await InsertNotificationAsync(connection, firstDraft);

        await connection.ExecuteAsync("DELETE FROM maintenance_drafts WHERE id = @firstDraft", new { firstDraft });
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM notifications"));

        await connection.ExecuteAsync("DELETE FROM email_attachments WHERE id = @attachmentId", new { attachmentId });
        Assert.Null(await connection.ExecuteScalarAsync<Guid?>("SELECT attachment_id FROM maintenance_drafts WHERE id = @secondDraft", new { secondDraft }));
    }

    private static async Task<List<string>> ColumnDefinitionsAsync(NpgsqlConnection connection) =>
        [.. await connection.QueryAsync<string>(
            "SELECT column_name || '|' || table_name || '|' || data_type || '|' || is_nullable || '|' || coalesce(column_default, '') " +
            "FROM information_schema.columns WHERE table_schema = 'public'")];

    private static async Task InsertOneRowInEachRecordTableAsync(NpgsqlConnection connection, Guid vehicleId)
    {
        const string audit = "now(), now(), 'o', 'o'";
        await connection.ExecuteAsync(
            "INSERT INTO service_records (id, vehicle_id, date_serviced, odometer_reading, created_at, updated_at, created_by, updated_by) " +
            "VALUES (gen_random_uuid(), @vehicleId, '2026-05-01', 100, " + audit + ")", new { vehicleId });
        await connection.ExecuteAsync(
            "INSERT INTO fuel_energy_entries (id, vehicle_id, kind, entry_date, unit_price, quantity, total_cost, created_at, updated_at, created_by, updated_by) " +
            "VALUES (gen_random_uuid(), @vehicleId, 'fuel', '2026-05-01', 3.459, 12.345, 42.70, " + audit + ")", new { vehicleId });
        await connection.ExecuteAsync(
            "INSERT INTO inspections (id, vehicle_id, inspection_type, inspection_date, cost, created_at, updated_at, created_by, updated_by) " +
            "VALUES (gen_random_uuid(), @vehicleId, 'safety', '2026-05-01', 25, " + audit + ")", new { vehicleId });
        await connection.ExecuteAsync(
            "INSERT INTO registrations (id, vehicle_id, state, cost, expiration_date, created_at, updated_at, created_by, updated_by) " +
            "VALUES (gen_random_uuid(), @vehicleId, 'WA', 78.75, '2027-05-01', " + audit + ")", new { vehicleId });
        await connection.ExecuteAsync(
            "INSERT INTO insurance_policies (id, vehicle_id, start_date, end_date, duration, cost, created_at, updated_at, created_by, updated_by) " +
            "VALUES (gen_random_uuid(), @vehicleId, '2026-01-01', '2027-01-01', 'annual', 1240, " + audit + ")", new { vehicleId });
        await connection.ExecuteAsync(
            "INSERT INTO taxes (id, vehicle_id, tax_type, amount, recurrence, created_at, updated_at, created_by, updated_by) " +
            "VALUES (gen_random_uuid(), @vehicleId, 'purchase', 1850, 'once', " + audit + ")", new { vehicleId });
    }

    private static async Task<Guid> InsertSubmissionAsync(NpgsqlConnection connection, string messageId, string status = "queued")
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO email_submissions (id, message_id, status, arrived_at, next_attempt_at) VALUES (@id, @messageId, @status, now(), now())",
            new { id, messageId, status });
        return id;
    }

    private static async Task<Guid> InsertAttachmentAsync(NpgsqlConnection connection, Guid submissionId, string contentType, int size)
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO email_attachments (id, submission_id, owner_id, file_name, content_type, size_bytes, sha256, created_at) " +
            "VALUES (@id, @submissionId, 'o', 'f.bin', @contentType, @size, repeat('a', 64), now())",
            new { id, submissionId, contentType, size });
        return id;
    }

    private static async Task<Guid> InsertDraftAsync(
        NpgsqlConnection connection,
        Guid submissionId,
        string? kind = "service_record",
        string state = "needs_review",
        string vehicleMatch = "none",
        decimal? confidence = 0.5m,
        Guid? vehicleId = null,
        Guid? attachmentId = null)
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO maintenance_drafts (id, owner_id, submission_id, attachment_id, state, kind, vehicle_id, vehicle_match, confidence, created_at, expires_at) " +
            "VALUES (@id, 'o', @submissionId, @attachmentId, @state, @kind, @vehicleId, @vehicleMatch, @confidence, now(), now() + interval '30 days')",
            new { id, submissionId, attachmentId, state, kind, vehicleId, vehicleMatch, confidence });
        return id;
    }

    private static Task InsertNotificationAsync(NpgsqlConnection connection, Guid draftId) =>
        connection.ExecuteAsync(
            "INSERT INTO notifications (id, owner_id, kind, draft_id, title, created_at) VALUES (gen_random_uuid(), 'o', 'draft_ready', @draftId, 't', now())",
            new { draftId });

    private static Task InsertDocumentAsync(NpgsqlConnection connection, Guid vehicleId, string contentType, int size) =>
        connection.ExecuteAsync(
            "INSERT INTO vehicle_documents (id, vehicle_id, file_name, content_type, size_bytes, created_at, created_by) " +
            "VALUES (gen_random_uuid(), @vehicleId, 'f.bin', @contentType, @size, now(), 'o')",
            new { vehicleId, contentType, size });

    private static readonly string[] AllMigrations =
    [
        "0001_create_vehicles", "0002_create_vehicle_tags", "0003_create_journal_notes", "0004_create_vehicle_images",
        "0005_create_lookup_items", "0006_add_vehicle_details", "0007_drop_vehicle_nickname", "0008_create_features",
        "0009_create_service_records", "0010_add_vehicle_latest_odometer", "0011_create_maintenance_records", "0012_create_email_intake",
        "0013_pricing_discounts_labor_tax", "0014_add_feature_category", "0015_add_upload_origin", "0016_create_reminders",
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

    private static async Task<Guid> InsertServiceRecordAsync(NpgsqlConnection connection, Guid vehicleId)
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO service_records (id, vehicle_id, date_serviced, odometer_reading, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @vehicleId, '2026-05-01', 100, now(), now(), 'o', 'o')",
            new { id, vehicleId });
        return id;
    }

    private static async Task<Guid> InsertLineItemAsync(NpgsqlConnection connection, Guid recordId)
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO service_line_items (id, service_record_id, description, total_cost, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @recordId, 'Oil change', 0, now(), now(), 'o', 'o')",
            new { id, recordId });
        return id;
    }

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
