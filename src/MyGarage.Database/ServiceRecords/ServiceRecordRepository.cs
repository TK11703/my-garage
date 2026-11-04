using Dapper;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.ServiceRecords;

// Every statement is scoped to the owner through the parent vehicle; service tables carry no owner column.
public sealed class ServiceRecordRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    // Null when the vehicle does not exist for that owner.
    public async Task<ServiceRecordDetailRecord?> AddAsync(
        string ownerId,
        Guid vehicleId,
        Guid recordId,
        DateOnly dateServiced,
        int odometerReading,
        string? shopOrPerson,
        decimal? taxAmount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var inserted = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            scripts.Get("ServiceRecords/Insert"),
            new
            {
                Id = recordId,
                VehicleId = vehicleId,
                OwnerId = ownerId,
                DateServiced = dateServiced,
                OdometerReading = odometerReading,
                ShopOrPerson = shopOrPerson,
                TaxAmount = taxAmount,
                Now = now,
                FromEmail = false,
            },
            cancellationToken: cancellationToken));

        return inserted is null ? null : await LoadAsync(connection, ownerId, vehicleId, recordId, cancellationToken);
    }

    // Most recent Date Serviced first. Null when the vehicle does not exist for that owner; a page past the last is empty.
    public async Task<PagedResult<ServiceRecordRecord>?> ListPageAsync(
        string ownerId,
        Guid vehicleId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var owns = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            scripts.Get("ServiceRecords/VehicleExists"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));
        if (!owns)
        {
            return null;
        }

        var items = (await connection.QueryAsync<ServiceRecordRecord>(new CommandDefinition(
            scripts.Get("ServiceRecords/ListPage"),
            new { VehicleId = vehicleId, OwnerId = ownerId, PageSize = pageSize, Offset = (page - 1) * pageSize },
            cancellationToken: cancellationToken))).ToList();
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            scripts.Get("ServiceRecords/Count"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));

        return new PagedResult<ServiceRecordRecord>(items, page, pageSize, total);
    }

    public async Task<ServiceRecordDetailRecord?> GetAsync(string ownerId, Guid vehicleId, Guid recordId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await LoadAsync(connection, ownerId, vehicleId, recordId, cancellationToken);
    }

    // Null when the record does not exist for that owner and vehicle.
    public async Task<ServiceRecordDetailRecord?> UpdateAsync(
        string ownerId,
        Guid vehicleId,
        Guid recordId,
        DateOnly dateServiced,
        int odometerReading,
        string? shopOrPerson,
        decimal? taxAmount,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            scripts.Get("ServiceRecords/Update"),
            new
            {
                RecordId = recordId,
                VehicleId = vehicleId,
                OwnerId = ownerId,
                DateServiced = dateServiced,
                OdometerReading = odometerReading,
                ShopOrPerson = shopOrPerson,
                TaxAmount = taxAmount,
                Now = now,
            },
            cancellationToken: cancellationToken));

        return updated is null ? null : await LoadAsync(connection, ownerId, vehicleId, recordId, cancellationToken);
    }

    // Line items and parts go with it through the cascading foreign keys.
    public async Task<bool> DeleteAsync(string ownerId, Guid vehicleId, Guid recordId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("ServiceRecords/Delete"),
            new { RecordId = recordId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken));
        return rows > 0;
    }

    // Records are limited to 50 line items; the record row is locked so concurrent adds cannot exceed it.
    public const int MaxLineItems = 50;

    public async Task<AddLineItemResult> AddLineItemAsync(
        string ownerId,
        Guid vehicleId,
        Guid recordId,
        Guid lineItemId,
        ServiceLineItemInput input,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            if (!await LockAsync(connection, transaction, ownerId, vehicleId, recordId, cancellationToken))
            {
                return new AddLineItemResult(AddLineItemOutcome.NotFound, null);
            }

            var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                scripts.Get("ServiceRecords/CountLineItems"), new { RecordId = recordId }, transaction, cancellationToken: cancellationToken));
            if (count >= MaxLineItems)
            {
                return new AddLineItemResult(AddLineItemOutcome.LimitReached, null);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                scripts.Get("ServiceRecords/InsertLineItem"),
                LineItemParameters(ownerId, vehicleId, recordId, lineItemId, input, now),
                transaction,
                cancellationToken: cancellationToken));
            await WritePartsAsync(connection, transaction, ownerId, vehicleId, recordId, lineItemId, input.Parts, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        var detail = await LoadAsync(connection, ownerId, vehicleId, recordId, cancellationToken);
        return new AddLineItemResult(AddLineItemOutcome.Created, detail);
    }

    // Replaces the line's description, pricing, and whole set of parts. Null when the line does not exist for that owner, vehicle, and record.
    public async Task<ServiceRecordDetailRecord?> UpdateLineItemAsync(
        string ownerId,
        Guid vehicleId,
        Guid recordId,
        Guid lineItemId,
        ServiceLineItemInput input,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            if (!await LockAsync(connection, transaction, ownerId, vehicleId, recordId, cancellationToken))
            {
                return null;
            }

            var rows = await connection.ExecuteAsync(new CommandDefinition(
                scripts.Get("ServiceRecords/UpdateLineItem"),
                LineItemParameters(ownerId, vehicleId, recordId, lineItemId, input, now),
                transaction,
                cancellationToken: cancellationToken));
            if (rows == 0)
            {
                return null;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                scripts.Get("ServiceRecords/DeleteParts"),
                new { LineItemId = lineItemId, RecordId = recordId, VehicleId = vehicleId, OwnerId = ownerId },
                transaction,
                cancellationToken: cancellationToken));
            await WritePartsAsync(connection, transaction, ownerId, vehicleId, recordId, lineItemId, input.Parts, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        return await LoadAsync(connection, ownerId, vehicleId, recordId, cancellationToken);
    }

    public async Task<bool> DeleteLineItemAsync(
        string ownerId, Guid vehicleId, Guid recordId, Guid lineItemId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("ServiceRecords/DeleteLineItem"),
            new { LineItemId = lineItemId, RecordId = recordId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken));
        return rows > 0;
    }

    private async Task<bool> LockAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string ownerId, Guid vehicleId, Guid recordId, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            scripts.Get("ServiceRecords/LockRecord"),
            new { RecordId = recordId, VehicleId = vehicleId, OwnerId = ownerId },
            transaction,
            cancellationToken: cancellationToken)) is not null;

    private static object LineItemParameters(
        string ownerId, Guid vehicleId, Guid recordId, Guid lineItemId, ServiceLineItemInput input, DateTimeOffset now) =>
        new
        {
            LineItemId = lineItemId,
            RecordId = recordId,
            VehicleId = vehicleId,
            OwnerId = ownerId,
            input.Description,
            input.FlatCost,
            input.LaborHours,
            input.LaborRate,
            LaborCost = input.StoredLaborCost,
            input.TotalCost,
            Now = now,
        };

    private async Task WritePartsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string ownerId,
        Guid vehicleId,
        Guid recordId,
        Guid lineItemId,
        IReadOnlyList<ServicePartInput> parts,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < parts.Count; index++)
        {
            var part = parts[index];
            await connection.ExecuteAsync(new CommandDefinition(
                scripts.Get("ServiceRecords/InsertPart"),
                new
                {
                    Id = Guid.CreateVersion7(),
                    LineItemId = lineItemId,
                    RecordId = recordId,
                    VehicleId = vehicleId,
                    OwnerId = ownerId,
                    Position = (short)(index + 1),
                    part.Name,
                    part.Quantity,
                    part.UnitPrice,
                },
                transaction,
                cancellationToken: cancellationToken));
        }
    }

    private async Task<ServiceRecordDetailRecord?> LoadAsync(
        NpgsqlConnection connection, string ownerId, Guid vehicleId, Guid recordId, CancellationToken cancellationToken)
    {
        var record = await connection.QuerySingleOrDefaultAsync<ServiceRecordRecord>(new CommandDefinition(
            scripts.Get("ServiceRecords/GetById"),
            new { RecordId = recordId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken));
        if (record is null)
        {
            return null;
        }

        var parameters = new { RecordId = recordId, VehicleId = vehicleId, OwnerId = ownerId };
        var lineItems = (await connection.QueryAsync<ServiceLineItemRecord>(new CommandDefinition(
            scripts.Get("ServiceRecords/ListLineItems"), parameters, cancellationToken: cancellationToken))).ToList();
        var parts = (await connection.QueryAsync<ServiceLinePartRecord>(new CommandDefinition(
            scripts.Get("ServiceRecords/ListParts"), parameters, cancellationToken: cancellationToken))).ToLookup(part => part.LineItemId);

        return new ServiceRecordDetailRecord(record, [.. lineItems.Select(line => line with { Parts = [.. parts[line.Id]] })]);
    }
}
