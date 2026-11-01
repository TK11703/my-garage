using Dapper;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.JournalNotes;

// Every statement is scoped to the owner through the parent vehicle.
public sealed class JournalNoteRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    // Null when the vehicle does not exist for that owner.
    public async Task<JournalNoteRecord?> AddAsync(
        string ownerId,
        Guid vehicleId,
        Guid noteId,
        string? category,
        string body,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<JournalNoteRecord>(new CommandDefinition(
            scripts.Get("JournalNotes/Insert"),
            new { Id = noteId, VehicleId = vehicleId, OwnerId = ownerId, Category = category, Body = body, Now = now },
            cancellationToken: cancellationToken));
    }

    // Newest first. Null when the vehicle does not exist for that owner; a page past the last is empty.
    public async Task<PagedResult<JournalNoteRecord>?> ListPageAsync(
        string ownerId,
        Guid vehicleId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var owns = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            scripts.Get("JournalNotes/VehicleExists"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));
        if (!owns)
        {
            return null;
        }

        var items = (await connection.QueryAsync<JournalNoteRecord>(new CommandDefinition(
            scripts.Get("JournalNotes/ListPage"),
            new { VehicleId = vehicleId, OwnerId = ownerId, PageSize = pageSize, Offset = (page - 1) * pageSize },
            cancellationToken: cancellationToken))).ToList();
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            scripts.Get("JournalNotes/Count"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));

        return new PagedResult<JournalNoteRecord>(items, page, pageSize, total);
    }

    public async Task<JournalNoteRecord?> UpdateAsync(
        string ownerId,
        Guid vehicleId,
        Guid noteId,
        string? category,
        string body,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<JournalNoteRecord>(new CommandDefinition(
            scripts.Get("JournalNotes/Update"),
            new { NoteId = noteId, VehicleId = vehicleId, OwnerId = ownerId, Category = category, Body = body, Now = now },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteAsync(string ownerId, Guid vehicleId, Guid noteId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("JournalNotes/Delete"), new { NoteId = noteId, VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));
        return rows > 0;
    }
}
