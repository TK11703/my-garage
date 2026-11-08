using Dapper;
using Npgsql;

namespace MyGarage.Database.Tests.Reminders;

// Direct SQL seeding so repository tests do not depend on the repositories under test.
public static class ReminderTestData
{
    public static async Task<Guid> InsertVehicleAsync(
        NpgsqlDataSource dataSource,
        string ownerId = "owner-a",
        int? mileage = null,
        int? latestOdometer = null,
        int? year = 2022,
        string make = "Ford",
        string model = "F-150")
    {
        var id = Guid.CreateVersion7();
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO vehicles (id, owner_id, make, model, year, mileage, latest_odometer_reading, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @ownerId, @make, @model, @year, @mileage, @latestOdometer, now(), now(), @ownerId, @ownerId)",
            new { id, ownerId, make, model, year, mileage, latestOdometer });
        return id;
    }

    public static async Task<Guid> InsertReminderAsync(
        NpgsqlDataSource dataSource,
        Guid vehicleId,
        string name = "Oil change",
        int? intervalMiles = 5000,
        int? intervalMonths = null,
        DateOnly? lastDoneOn = null,
        int? lastDoneMileage = 40_000,
        bool isPaused = false,
        string? notifiedState = null,
        DateTimeOffset? createdAt = null)
    {
        var id = Guid.CreateVersion7();
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO reminders (id, vehicle_id, name, interval_miles, interval_months, last_done_on, last_done_mileage, is_paused, notified_state, " +
            "created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @vehicleId, @name, @intervalMiles, @intervalMonths, @lastDoneOn, @lastDoneMileage, @isPaused, @notifiedState, @at, @at, 'seed', 'seed')",
            new
            {
                id, vehicleId, name, intervalMiles, intervalMonths,
                lastDoneOn = lastDoneOn ?? new DateOnly(2026, 5, 1), lastDoneMileage, isPaused, notifiedState,
                at = createdAt ?? new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero),
            });
        return id;
    }

    public static async Task<Guid> InsertCompletionAsync(
        NpgsqlDataSource dataSource,
        Guid reminderId,
        DateOnly? doneOn = null,
        int? mileage = 45_000,
        DateTimeOffset? createdAt = null)
    {
        var id = Guid.CreateVersion7();
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO reminder_completions (id, reminder_id, done_on, mileage, created_at, created_by) VALUES (@id, @reminderId, @doneOn, @mileage, @at, 'seed')",
            new { id, reminderId, doneOn = doneOn ?? new DateOnly(2026, 9, 1), mileage, at = createdAt ?? DateTimeOffset.UtcNow });
        return id;
    }

    public static async Task<int> CountAsync(NpgsqlDataSource dataSource, string table)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>($"SELECT count(*)::int FROM {table}");
    }
}