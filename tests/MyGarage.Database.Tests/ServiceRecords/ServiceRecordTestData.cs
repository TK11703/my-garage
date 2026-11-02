using Dapper;
using Npgsql;

namespace MyGarage.Database.Tests.ServiceRecords;

// Direct SQL seeding so repository tests do not depend on the repository under test.
public static class ServiceRecordTestData
{
    public static async Task<Guid> InsertRecordAsync(
        NpgsqlDataSource dataSource,
        Guid vehicleId,
        DateOnly dateServiced,
        int odometer = 1000,
        string? shop = null,
        DateTimeOffset? createdAt = null)
    {
        var id = Guid.CreateVersion7();
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO service_records (id, vehicle_id, date_serviced, odometer_reading, shop_or_person, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @vehicleId, @dateServiced, @odometer, @shop, @at, @at, 'seed', 'seed')",
            new { id, vehicleId, dateServiced, odometer, shop, at = createdAt ?? DateTimeOffset.UtcNow });
        return id;
    }

    public static async Task<Guid> InsertLineItemAsync(
        NpgsqlDataSource dataSource,
        Guid recordId,
        string description = "Oil change",
        decimal flatCost = 10m,
        DateTimeOffset? createdAt = null)
    {
        var id = Guid.CreateVersion7();
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO service_line_items (id, service_record_id, description, flat_cost, total_cost, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @recordId, @description, @flatCost, @flatCost, @at, @at, 'seed', 'seed')",
            new { id, recordId, description, flatCost, at = createdAt ?? DateTimeOffset.UtcNow });
        return id;
    }

    public static async Task<int> CountAsync(NpgsqlDataSource dataSource, string table)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>($"SELECT count(*) FROM {table}");
    }
}
