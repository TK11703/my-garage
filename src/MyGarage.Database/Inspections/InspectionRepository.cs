using Dapper;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.Inspections;

// Every statement is scoped to the owner through the parent vehicle; the table carries no owner column.
public sealed class InspectionRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    // Null when the vehicle does not exist for that owner.
    public async Task<InspectionRecord?> AddAsync(
        string ownerId,
        Guid vehicleId,
        Guid id,
        string inspectionType,
        DateOnly inspectionDate,
        decimal cost,
        DateOnly? nextDueDate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InspectionRecord>(new CommandDefinition(
            scripts.Get("Inspections/Insert"),
            new { Id = id, VehicleId = vehicleId, OwnerId = ownerId, InspectionType = inspectionType, InspectionDate = inspectionDate, Cost = cost, NextDueDate = nextDueDate, Now = now },
            cancellationToken: cancellationToken));
    }

    // Newest inspection date first. Null when the vehicle does not exist for that owner; a page past the last is empty.
    public async Task<PagedResult<InspectionRecord>?> ListPageAsync(
        string ownerId,
        Guid vehicleId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var owns = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            scripts.Get("Inspections/VehicleExists"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));
        if (!owns)
        {
            return null;
        }

        var items = (await connection.QueryAsync<InspectionRecord>(new CommandDefinition(
            scripts.Get("Inspections/ListPage"),
            new { VehicleId = vehicleId, OwnerId = ownerId, PageSize = pageSize, Offset = (page - 1) * pageSize },
            cancellationToken: cancellationToken))).ToList();
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            scripts.Get("Inspections/Count"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));

        return new PagedResult<InspectionRecord>(items, page, pageSize, total);
    }

    public async Task<InspectionRecord?> GetAsync(string ownerId, Guid vehicleId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InspectionRecord>(new CommandDefinition(
            scripts.Get("Inspections/GetById"),
            new { EntryId = entryId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken));
    }

    // Null when the inspection does not exist for that owner and vehicle.
    public async Task<InspectionRecord?> UpdateAsync(
        string ownerId,
        Guid vehicleId,
        Guid entryId,
        string inspectionType,
        DateOnly inspectionDate,
        decimal cost,
        DateOnly? nextDueDate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InspectionRecord>(new CommandDefinition(
            scripts.Get("Inspections/Update"),
            new { EntryId = entryId, VehicleId = vehicleId, OwnerId = ownerId, InspectionType = inspectionType, InspectionDate = inspectionDate, Cost = cost, NextDueDate = nextDueDate, Now = now },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteAsync(string ownerId, Guid vehicleId, Guid entryId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Inspections/Delete"),
            new { EntryId = entryId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken));
        return rows > 0;
    }
}
