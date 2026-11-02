using Dapper;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.Lookups;

public sealed class LookupRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    // Defaults first in their defined order, then the owner's own items alphabetically, per kind.
    public async Task<IReadOnlyList<LookupItemRecord>> ListAsync(string ownerId, bool includeHidden, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var items = await connection.QueryAsync<LookupItemRecord>(new CommandDefinition(
            scripts.Get("Lookups/List"),
            new { OwnerId = ownerId, IncludeHidden = includeHidden },
            cancellationToken: cancellationToken));
        return items.ToList();
    }

    public const int MaxCustomItemsPerKind = 50;

    // Adds an item private to the owner. The advisory lock serialises concurrent adds so the limit and the duplicate check hold.
    public async Task<AddLookupResult> AddCustomAsync(string ownerId, string kind, string name, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Lookups/LockOwnerKind"), new { OwnerId = ownerId, Kind = kind }, transaction, cancellationToken: cancellationToken));

        var existing = await connection.QuerySingleOrDefaultAsync<LookupItemRecord>(new CommandDefinition(
            scripts.Get("Lookups/FindByName"), new { OwnerId = ownerId, Kind = kind, Name = trimmed }, transaction, cancellationToken: cancellationToken));
        if (existing is not null)
        {
            return new AddLookupResult(AddLookupOutcome.Duplicate, ExistingIsHidden: existing.IsHidden);
        }

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            scripts.Get("Lookups/CountCustom"), new { OwnerId = ownerId, Kind = kind }, transaction, cancellationToken: cancellationToken));
        if (count >= MaxCustomItemsPerKind)
        {
            return new AddLookupResult(AddLookupOutcome.LimitReached);
        }

        var id = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Lookups/Insert"),
            new { Id = id, Kind = kind, OwnerId = ownerId, Name = trimmed, CreatedAt = now },
            transaction,
            cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return new AddLookupResult(
            AddLookupOutcome.Created,
            new LookupItemRecord { Id = id, Kind = kind, Name = trimmed, IsDefault = false, IsHidden = false });
    }

    // Hides an item the owner can see (a default or their own) of that kind; null when there is no such item. Idempotent.
    public async Task<LookupItemRecord?> HideAsync(string ownerId, string kind, Guid itemId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var item = await GetVisibleAsync(connection, ownerId, kind, itemId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Lookups/Hide"), new { OwnerId = ownerId, ItemId = itemId, HiddenAt = now }, cancellationToken: cancellationToken));
        return item with { IsHidden = true };
    }

    // Shows an item again; null when there is no such item for the owner. Idempotent.
    public async Task<LookupItemRecord?> ShowAsync(string ownerId, string kind, Guid itemId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var item = await GetVisibleAsync(connection, ownerId, kind, itemId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Lookups/Show"), new { OwnerId = ownerId, ItemId = itemId }, cancellationToken: cancellationToken));
        return item with { IsHidden = false };
    }

    private async Task<LookupItemRecord?> GetVisibleAsync(NpgsqlConnection connection, string ownerId, string kind, Guid itemId, CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<LookupItemRecord>(new CommandDefinition(
            scripts.Get("Lookups/GetVisibleItem"), new { OwnerId = ownerId, Kind = kind, ItemId = itemId }, cancellationToken: cancellationToken));

    // The requested items that are a default or owned by the caller; anything else (unknown id, another owner's item) is simply absent.
    public async Task<IReadOnlyList<LookupItemRecord>> FindAvailableAsync(string ownerId, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
        {
            return [];
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var items = await connection.QueryAsync<LookupItemRecord>(new CommandDefinition(
            scripts.Get("Lookups/FindAvailable"),
            new { OwnerId = ownerId, ItemIds = itemIds.ToArray() },
            cancellationToken: cancellationToken));
        return items.ToList();
    }
}
