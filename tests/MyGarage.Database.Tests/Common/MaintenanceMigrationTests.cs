using Dapper;
using Npgsql;

namespace MyGarage.Database.Tests.Common;

[Collection(DatabaseCollection.Name)]
public class MaintenanceMigrationTests(PostgresFixture postgres)
{
    private static async Task<Guid> InsertVehicleAsync(NpgsqlConnection connection)
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO vehicles (id, owner_id, make, model, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, 'o', 'Ford', 'F-150', now(), now(), 'o', 'o')", new { id });
        return id;
    }

    // Each case replaces one column's value in an otherwise valid row; the insert must be rejected.
    public static TheoryData<string, string, string> InvalidRows => new()
    {
        { "fuel_energy_entries", "kind", "'diesel'" },
        { "fuel_energy_entries", "entry_date", "'1899-12-31'" },
        { "fuel_energy_entries", "unit_price", "-0.0001" },
        { "fuel_energy_entries", "unit_price", "10000.0000" },
        { "fuel_energy_entries", "quantity", "0" },
        { "fuel_energy_entries", "quantity", "100000.000" },
        { "fuel_energy_entries", "total_cost", "-0.01" },
        { "fuel_energy_entries", "total_cost", "10000000.00" },
        { "inspections", "inspection_type", "'other'" },
        { "inspections", "inspection_date", "'1899-12-31'" },
        { "inspections", "cost", "-0.01" },
        { "inspections", "cost", "10000000.00" },
        { "inspections", "next_due_date", "'2026-04-30'" },
        { "inspections", "next_due_date", "'2101-01-01'" },
        { "registrations", "state", "'wa'" },
        { "registrations", "state", "'W'" },
        { "registrations", "cost", "-0.01" },
        { "registrations", "expiration_date", "'1899-12-31'" },
        { "registrations", "expiration_date", "'2101-01-01'" },
        { "insurance_policies", "start_date", "'1899-12-31'" },
        { "insurance_policies", "start_date", "'2101-01-01'" },
        { "insurance_policies", "end_date", "'2101-01-01'" },
        { "insurance_policies", "end_date", "'2026-01-01'" },
        { "insurance_policies", "end_date", "'2025-12-31'" },
        { "insurance_policies", "duration", "'weekly'" },
        { "insurance_policies", "cost", "-0.01" },
        { "insurance_policies", "cost", "10000000.00" },
        { "taxes", "tax_type", "'sales'" },
        { "taxes", "amount", "0" },
        { "taxes", "amount", "10000000.00" },
        { "taxes", "recurrence", "'weekly'" },
    };

    private static readonly Dictionary<string, Dictionary<string, string>> ValidRows = new()
    {
        ["fuel_energy_entries"] = new() { ["kind"] = "'fuel'", ["entry_date"] = "'2026-05-01'", ["unit_price"] = "3.459", ["quantity"] = "12.345", ["total_cost"] = "42.70" },
        ["inspections"] = new() { ["inspection_type"] = "'safety'", ["inspection_date"] = "'2026-05-01'", ["cost"] = "25", ["next_due_date"] = "null" },
        ["registrations"] = new() { ["state"] = "'WA'", ["cost"] = "78.75", ["expiration_date"] = "'2027-05-01'" },
        ["insurance_policies"] = new() { ["start_date"] = "'2026-01-01'", ["end_date"] = "'2027-01-01'", ["duration"] = "'annual'", ["cost"] = "1240" },
        ["taxes"] = new() { ["tax_type"] = "'purchase'", ["amount"] = "1850", ["recurrence"] = "'once'" },
    };

    private static string InsertSql(string table, IReadOnlyDictionary<string, string> values) =>
        $"INSERT INTO {table} (id, vehicle_id, {string.Join(", ", values.Keys)}, created_at, updated_at, created_by, updated_by) " +
        $"VALUES (gen_random_uuid(), @vehicleId, {string.Join(", ", values.Values)}, now(), now(), 'o', 'o')";

    [Theory]
    [MemberData(nameof(InvalidRows))]
    public async Task Schema_Checks_RejectInvalidValues(string table, string column, string value)
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var vehicleId = await InsertVehicleAsync(connection);
        var values = new Dictionary<string, string>(ValidRows[table]) { [column] = value };

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(InsertSql(table, values), new { vehicleId }));
    }

    [Fact]
    public async Task Schema_ValidRowsAndBoundaryValues_AreAccepted()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var vehicleId = await InsertVehicleAsync(connection);

        foreach (var (table, values) in ValidRows)
        {
            await connection.ExecuteAsync(InsertSql(table, values), new { vehicleId });
        }

        // Boundaries: a free charging session, a next due date equal to the inspection date, and one far in the future.
        await connection.ExecuteAsync(InsertSql("fuel_energy_entries", new Dictionary<string, string>(ValidRows["fuel_energy_entries"]) { ["unit_price"] = "0", ["total_cost"] = "0" }), new { vehicleId });
        await connection.ExecuteAsync(InsertSql("inspections", new Dictionary<string, string>(ValidRows["inspections"]) { ["next_due_date"] = "'2026-05-01'" }), new { vehicleId });
        await connection.ExecuteAsync(InsertSql("inspections", new Dictionary<string, string>(ValidRows["inspections"]) { ["next_due_date"] = "'2100-12-31'" }), new { vehicleId });
        await connection.ExecuteAsync(InsertSql("insurance_policies", new Dictionary<string, string>(ValidRows["insurance_policies"]) { ["end_date"] = "'2026-01-02'" }), new { vehicleId });

        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM fuel_energy_entries"));
        Assert.Equal(3, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM inspections"));
        Assert.Equal("WA", await connection.ExecuteScalarAsync<string>("SELECT state FROM registrations"));
    }

    [Fact]
    public async Task Schema_ExpectedIndexesExist()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();

        var indexes = (await connection.QueryAsync<string>("SELECT indexname FROM pg_indexes WHERE schemaname = 'public'")).ToHashSet();

        Assert.Superset(
            new HashSet<string>
            {
                "ix_fuel_energy_entries_vehicle_date", "ix_inspections_vehicle_date", "ix_registrations_vehicle_expiration",
                "ix_insurance_policies_vehicle_end", "ix_taxes_vehicle_created",
            },
            indexes);
    }

    [Fact]
    public async Task Schema_DeletingAVehicle_CascadesToAllFiveTables()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var vehicleId = await InsertVehicleAsync(connection);
        var otherVehicleId = await InsertVehicleAsync(connection);
        foreach (var (table, values) in ValidRows)
        {
            await connection.ExecuteAsync(InsertSql(table, values), new { vehicleId });
            await connection.ExecuteAsync(InsertSql(table, values), new { vehicleId = otherVehicleId });
        }

        await connection.ExecuteAsync("DELETE FROM vehicles WHERE id = @vehicleId", new { vehicleId });

        foreach (var table in ValidRows.Keys)
        {
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table}"));
            Assert.Equal(1, await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table} WHERE vehicle_id = @otherVehicleId", new { otherVehicleId }));
        }
    }
}
