using Dapper;
using Npgsql;

namespace MyGarage.Database.Tests.Common;

// Direct SQL seeding so repository tests do not depend on the repositories under test.
public static class MaintenanceTestData
{
    public static async Task<Guid> InsertFuelEnergyEntryAsync(
        NpgsqlDataSource dataSource,
        Guid vehicleId,
        string kind = "fuel",
        DateOnly? entryDate = null,
        decimal unitPrice = 3.459m,
        decimal quantity = 12.345m,
        decimal totalCost = 42.70m,
        DateTimeOffset? createdAt = null) =>
        await InsertAsync(
            dataSource,
            "INSERT INTO fuel_energy_entries (id, vehicle_id, kind, entry_date, unit_price, quantity, total_cost, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @vehicleId, @kind, @entryDate, @unitPrice, @quantity, @totalCost, @at, @at, 'seed', 'seed')",
            new { vehicleId, kind, entryDate = entryDate ?? new DateOnly(2026, 5, 1), unitPrice, quantity, totalCost },
            createdAt);

    public static async Task<Guid> InsertInspectionAsync(
        NpgsqlDataSource dataSource,
        Guid vehicleId,
        string type = "safety",
        DateOnly? inspectionDate = null,
        decimal cost = 25m,
        DateOnly? nextDueDate = null,
        DateTimeOffset? createdAt = null) =>
        await InsertAsync(
            dataSource,
            "INSERT INTO inspections (id, vehicle_id, inspection_type, inspection_date, cost, next_due_date, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @vehicleId, @type, @inspectionDate, @cost, @nextDueDate, @at, @at, 'seed', 'seed')",
            new { vehicleId, type, inspectionDate = inspectionDate ?? new DateOnly(2026, 5, 1), cost, nextDueDate },
            createdAt);

    public static async Task<Guid> InsertRegistrationAsync(
        NpgsqlDataSource dataSource,
        Guid vehicleId,
        string state = "WA",
        decimal cost = 78.75m,
        DateOnly? expirationDate = null,
        DateTimeOffset? createdAt = null) =>
        await InsertAsync(
            dataSource,
            "INSERT INTO registrations (id, vehicle_id, state, cost, expiration_date, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @vehicleId, @state, @cost, @expirationDate, @at, @at, 'seed', 'seed')",
            new { vehicleId, state, cost, expirationDate = expirationDate ?? new DateOnly(2027, 5, 1) },
            createdAt);

    public static async Task<Guid> InsertInsurancePolicyAsync(
        NpgsqlDataSource dataSource,
        Guid vehicleId,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        string duration = "annual",
        decimal cost = 1240m,
        DateTimeOffset? createdAt = null) =>
        await InsertAsync(
            dataSource,
            "INSERT INTO insurance_policies (id, vehicle_id, start_date, end_date, duration, cost, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @vehicleId, @startDate, @endDate, @duration, @cost, @at, @at, 'seed', 'seed')",
            new { vehicleId, startDate = startDate ?? new DateOnly(2026, 1, 1), endDate = endDate ?? new DateOnly(2027, 1, 1), duration, cost },
            createdAt);

    public static async Task<Guid> InsertTaxAsync(
        NpgsqlDataSource dataSource,
        Guid vehicleId,
        string type = "personal_property",
        decimal amount = 312.40m,
        string recurrence = "annual",
        DateTimeOffset? createdAt = null) =>
        await InsertAsync(
            dataSource,
            "INSERT INTO taxes (id, vehicle_id, tax_type, amount, recurrence, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @vehicleId, @type, @amount, @recurrence, @at, @at, 'seed', 'seed')",
            new { vehicleId, type, amount, recurrence },
            createdAt);

    public static async Task<int> CountAsync(NpgsqlDataSource dataSource, string table)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table}");
    }

    private static async Task<Guid> InsertAsync(NpgsqlDataSource dataSource, string sql, object values, DateTimeOffset? createdAt)
    {
        var id = Guid.CreateVersion7();
        var parameters = new DynamicParameters(values);
        parameters.Add("id", id);
        parameters.Add("at", createdAt ?? DateTimeOffset.UtcNow);
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, parameters);
        return id;
    }
}
