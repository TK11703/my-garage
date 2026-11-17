using Dapper;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.FuelEnergyEntries;

// Every statement is scoped to the owner through the parent vehicle; the table carries no owner column.
public sealed class FuelEnergyEntryRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    // Null when the vehicle does not exist for that owner.
    public async Task<FuelEnergyEntryRecord?> AddAsync(
        string ownerId,
        Guid vehicleId,
        Guid id,
        string kind,
        DateOnly entryDate,
        decimal unitPrice,
        decimal quantity,
        decimal totalCost,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<FuelEnergyEntryRecord>(new CommandDefinition(
            scripts.Get("FuelEnergyEntries/Insert"),
            new { Id = id, VehicleId = vehicleId, OwnerId = ownerId, Kind = kind, EntryDate = entryDate, UnitPrice = unitPrice, Quantity = quantity, TotalCost = totalCost, Now = now, FromEmail = false },
            cancellationToken: cancellationToken));
    }

    // Newest entry date first. Null when the vehicle does not exist for that owner; a page past the last is empty.
    public async Task<PagedResult<FuelEnergyEntryRecord>?> ListPageAsync(
        string ownerId,
        Guid vehicleId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var owns = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            scripts.Get("FuelEnergyEntries/VehicleExists"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));
        if (!owns)
        {
            return null;
        }

        var items = (await connection.QueryAsync<FuelEnergyEntryRecord>(new CommandDefinition(
            scripts.Get("FuelEnergyEntries/ListPage"),
            new { VehicleId = vehicleId, OwnerId = ownerId, PageSize = pageSize, Offset = (page - 1) * pageSize },
            cancellationToken: cancellationToken))).ToList();
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            scripts.Get("FuelEnergyEntries/Count"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));

        return new PagedResult<FuelEnergyEntryRecord>(items, page, pageSize, total);
    }

    // Months with no entries are absent. Null when the vehicle does not exist for that owner.
    public async Task<IReadOnlyList<FuelEnergyMonthRecord>?> GetMonthlySpendAsync(
        string ownerId,
        Guid vehicleId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var owns = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            scripts.Get("FuelEnergyEntries/VehicleExists"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));
        if (!owns)
        {
            return null;
        }

        return [.. await connection.QueryAsync<FuelEnergyMonthRecord>(new CommandDefinition(
            scripts.Get("FuelEnergyEntries/MonthlySpend"),
            new { VehicleId = vehicleId, OwnerId = ownerId, From = from, To = to },
            cancellationToken: cancellationToken))];
    }

    public async Task<FuelEnergyEntryRecord?> GetAsync(string ownerId, Guid vehicleId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<FuelEnergyEntryRecord>(new CommandDefinition(
            scripts.Get("FuelEnergyEntries/GetById"),
            new { EntryId = entryId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken));
    }

    // Null when the entry does not exist for that owner and vehicle.
    public async Task<FuelEnergyEntryRecord?> UpdateAsync(
        string ownerId,
        Guid vehicleId,
        Guid entryId,
        string kind,
        DateOnly entryDate,
        decimal unitPrice,
        decimal quantity,
        decimal totalCost,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<FuelEnergyEntryRecord>(new CommandDefinition(
            scripts.Get("FuelEnergyEntries/Update"),
            new { EntryId = entryId, VehicleId = vehicleId, OwnerId = ownerId, Kind = kind, EntryDate = entryDate, UnitPrice = unitPrice, Quantity = quantity, TotalCost = totalCost, Now = now },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteAsync(string ownerId, Guid vehicleId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("FuelEnergyEntries/Delete"),
            new { EntryId = entryId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken));
        return rows > 0;
    }
}
