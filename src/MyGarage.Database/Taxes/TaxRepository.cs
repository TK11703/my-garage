using Dapper;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.Taxes;

// Every statement is scoped to the owner through the parent vehicle; the table carries no owner column.
public sealed class TaxRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    // Null when the vehicle does not exist for that owner.
    public async Task<TaxRecord?> AddAsync(
        string ownerId,
        Guid vehicleId,
        Guid id,
        string taxType,
        decimal amount,
        string recurrence,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<TaxRecord>(new CommandDefinition(
            scripts.Get("Taxes/Insert"),
            new { Id = id, VehicleId = vehicleId, OwnerId = ownerId, TaxType = taxType, Amount = amount, Recurrence = recurrence, Now = now, FromEmail = false },
            cancellationToken: cancellationToken));
    }

    // Most recently added first. Null when the vehicle does not exist for that owner; a page past the last is empty.
    public async Task<PagedResult<TaxRecord>?> ListPageAsync(
        string ownerId,
        Guid vehicleId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var owns = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            scripts.Get("Taxes/VehicleExists"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));
        if (!owns)
        {
            return null;
        }

        var items = (await connection.QueryAsync<TaxRecord>(new CommandDefinition(
            scripts.Get("Taxes/ListPage"),
            new { VehicleId = vehicleId, OwnerId = ownerId, PageSize = pageSize, Offset = (page - 1) * pageSize },
            cancellationToken: cancellationToken))).ToList();
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            scripts.Get("Taxes/Count"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));

        return new PagedResult<TaxRecord>(items, page, pageSize, total);
    }

    public async Task<TaxRecord?> GetAsync(string ownerId, Guid vehicleId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<TaxRecord>(new CommandDefinition(
            scripts.Get("Taxes/GetById"),
            new { EntryId = entryId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken));
    }

    // Null when the tax does not exist for that owner and vehicle.
    public async Task<TaxRecord?> UpdateAsync(
        string ownerId,
        Guid vehicleId,
        Guid entryId,
        string taxType,
        decimal amount,
        string recurrence,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<TaxRecord>(new CommandDefinition(
            scripts.Get("Taxes/Update"),
            new { EntryId = entryId, VehicleId = vehicleId, OwnerId = ownerId, TaxType = taxType, Amount = amount, Recurrence = recurrence, Now = now },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteAsync(string ownerId, Guid vehicleId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Taxes/Delete"),
            new { EntryId = entryId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken));
        return rows > 0;
    }
}
