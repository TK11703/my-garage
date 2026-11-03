using Dapper;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.Registrations;

// Every statement is scoped to the owner through the parent vehicle; the table carries no owner column.
public sealed class RegistrationRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    // Null when the vehicle does not exist for that owner.
    public async Task<RegistrationRecord?> AddAsync(
        string ownerId,
        Guid vehicleId,
        Guid id,
        string state,
        decimal cost,
        DateOnly expirationDate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<RegistrationRecord>(new CommandDefinition(
            scripts.Get("Registrations/Insert"),
            new { Id = id, VehicleId = vehicleId, OwnerId = ownerId, State = state, Cost = cost, ExpirationDate = expirationDate, Now = now },
            cancellationToken: cancellationToken));
    }

    // Latest expiration date first. Null when the vehicle does not exist for that owner; a page past the last is empty.
    public async Task<PagedResult<RegistrationRecord>?> ListPageAsync(
        string ownerId,
        Guid vehicleId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var owns = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            scripts.Get("Registrations/VehicleExists"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));
        if (!owns)
        {
            return null;
        }

        var items = (await connection.QueryAsync<RegistrationRecord>(new CommandDefinition(
            scripts.Get("Registrations/ListPage"),
            new { VehicleId = vehicleId, OwnerId = ownerId, PageSize = pageSize, Offset = (page - 1) * pageSize },
            cancellationToken: cancellationToken))).ToList();
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            scripts.Get("Registrations/Count"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));

        return new PagedResult<RegistrationRecord>(items, page, pageSize, total);
    }

    public async Task<RegistrationRecord?> GetAsync(string ownerId, Guid vehicleId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<RegistrationRecord>(new CommandDefinition(
            scripts.Get("Registrations/GetById"),
            new { EntryId = entryId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken));
    }

    // Null when the registration does not exist for that owner and vehicle.
    public async Task<RegistrationRecord?> UpdateAsync(
        string ownerId,
        Guid vehicleId,
        Guid entryId,
        string state,
        decimal cost,
        DateOnly expirationDate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<RegistrationRecord>(new CommandDefinition(
            scripts.Get("Registrations/Update"),
            new { EntryId = entryId, VehicleId = vehicleId, OwnerId = ownerId, State = state, Cost = cost, ExpirationDate = expirationDate, Now = now },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteAsync(string ownerId, Guid vehicleId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Registrations/Delete"),
            new { EntryId = entryId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken));
        return rows > 0;
    }
}
