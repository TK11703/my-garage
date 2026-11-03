using Dapper;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.Insurance;

// Every statement is scoped to the owner through the parent vehicle; the table carries no owner column.
public sealed class InsurancePolicyRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    // Null when the vehicle does not exist for that owner.
    public async Task<InsurancePolicyRecord?> AddAsync(
        string ownerId,
        Guid vehicleId,
        Guid id,
        DateOnly startDate,
        DateOnly endDate,
        string duration,
        decimal cost,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InsurancePolicyRecord>(new CommandDefinition(
            scripts.Get("Insurance/Insert"),
            new { Id = id, VehicleId = vehicleId, OwnerId = ownerId, StartDate = startDate, EndDate = endDate, Duration = duration, Cost = cost, Now = now, FromEmail = false },
            cancellationToken: cancellationToken));
    }

    // Latest end date first. Null when the vehicle does not exist for that owner; a page past the last is empty.
    public async Task<PagedResult<InsurancePolicyRecord>?> ListPageAsync(
        string ownerId,
        Guid vehicleId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var owns = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            scripts.Get("Insurance/VehicleExists"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));
        if (!owns)
        {
            return null;
        }

        var items = (await connection.QueryAsync<InsurancePolicyRecord>(new CommandDefinition(
            scripts.Get("Insurance/ListPage"),
            new { VehicleId = vehicleId, OwnerId = ownerId, PageSize = pageSize, Offset = (page - 1) * pageSize },
            cancellationToken: cancellationToken))).ToList();
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            scripts.Get("Insurance/Count"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));

        return new PagedResult<InsurancePolicyRecord>(items, page, pageSize, total);
    }

    public async Task<InsurancePolicyRecord?> GetAsync(string ownerId, Guid vehicleId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InsurancePolicyRecord>(new CommandDefinition(
            scripts.Get("Insurance/GetById"),
            new { EntryId = entryId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken));
    }

    // Null when the policy does not exist for that owner and vehicle.
    public async Task<InsurancePolicyRecord?> UpdateAsync(
        string ownerId,
        Guid vehicleId,
        Guid entryId,
        DateOnly startDate,
        DateOnly endDate,
        string duration,
        decimal cost,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InsurancePolicyRecord>(new CommandDefinition(
            scripts.Get("Insurance/Update"),
            new { EntryId = entryId, VehicleId = vehicleId, OwnerId = ownerId, StartDate = startDate, EndDate = endDate, Duration = duration, Cost = cost, Now = now },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteAsync(string ownerId, Guid vehicleId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Insurance/Delete"),
            new { EntryId = entryId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken));
        return rows > 0;
    }
}
