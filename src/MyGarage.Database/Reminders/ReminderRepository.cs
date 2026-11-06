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

    // Adds a completion and rolls the last-done values forward in one transaction; null when the reminder is not the owner's.
    // With raiseVehicleOdometer the vehicle's latest odometer reading is raised to the mileage, never lowered.
    public async Task<ReminderRecord?> CompleteAsync(
        string ownerId,
        Guid vehicleId,
        Guid reminderId,
        Guid completionId,
        DateOnly doneOn,
        int? mileage,
        bool raiseVehicleOdometer,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var rolled = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            scripts.Get("Reminders/UpdateLastDone"),
            new { ReminderId = reminderId, VehicleId = vehicleId, OwnerId = ownerId, DoneOn = doneOn, Mileage = mileage, Now = now },
            transaction,
            cancellationToken: cancellationToken));
        if (rolled is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Reminders/InsertCompletion"),
            new { CompletionId = completionId, ReminderId = reminderId, OwnerId = ownerId, DoneOn = doneOn, Mileage = mileage, Now = now },
            transaction,
            cancellationToken: cancellationToken));

        if (raiseVehicleOdometer && mileage is not null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                scripts.Get("Reminders/RaiseVehicleOdometer"),
                new { VehicleId = vehicleId, OwnerId = ownerId, Mileage = mileage, Now = now },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
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
