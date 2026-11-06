using Dapper;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.Reminders;

// Every statement is scoped to the owner through the parent vehicle; the tables carry no owner column.
public sealed class ReminderRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    // Null when the vehicle does not exist for that owner.
    public async Task<ReminderRecord?> AddAsync(
        string ownerId,
        Guid vehicleId,
        Guid id,
        ReminderValues values,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var inserted = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            scripts.Get("Reminders/Insert"),
            new
            {
                Id = id, VehicleId = vehicleId, OwnerId = ownerId, values.Name, values.AppliesTo, values.Subject,
                values.IntervalMiles, values.IntervalMonths, values.LastDoneOn, values.LastDoneMileage, Now = now,
            },
            cancellationToken: cancellationToken));

        return inserted is null ? null : await GetAsync(connection, ownerId, vehicleId, id, cancellationToken);
    }

    // Oldest first. Null when the vehicle does not exist for that owner.
    public async Task<IReadOnlyList<ReminderRecord>?> ListForVehicleAsync(string ownerId, Guid vehicleId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var owns = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            scripts.Get("Reminders/VehicleExists"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));
        if (!owns)
        {
            return null;
        }

        return (await connection.QueryAsync<ReminderRecord>(new CommandDefinition(
            scripts.Get("Reminders/ListForVehicle"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken))).ToList();
    }

    public async Task<ReminderRecord?> GetAsync(string ownerId, Guid vehicleId, Guid reminderId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await GetAsync(connection, ownerId, vehicleId, reminderId, cancellationToken);
    }

    // Every reminder of every vehicle the owner has, oldest first.
    public async Task<IReadOnlyList<ReminderRecord>> ListForOwnerAsync(string ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<ReminderRecord>(new CommandDefinition(
            scripts.Get("Reminders/ListForOwner"), new { OwnerId = ownerId }, cancellationToken: cancellationToken))).ToList();
    }

    // Most recent first; empty for a reminder that is not the owner's.
    public async Task<IReadOnlyList<ReminderCompletionRecord>> ListCompletionsAsync(
        string ownerId,
        Guid vehicleId,
        Guid reminderId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<ReminderCompletionRecord>(new CommandDefinition(
            scripts.Get("Reminders/ListCompletions"),
            new { ReminderId = reminderId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken))).ToList();
    }

    private async Task<ReminderRecord?> GetAsync(
        NpgsqlConnection connection,
        string ownerId,
        Guid vehicleId,
        Guid reminderId,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<ReminderRecord>(new CommandDefinition(
            scripts.Get("Reminders/GetById"),
            new { ReminderId = reminderId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken));
}
