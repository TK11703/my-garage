using Dapper;
using Npgsql;

namespace MyGarage.Database.Tests.Lookups;

// Seeds lookup rows straight into the database so repository tests do not depend on the code under test.
public static class LookupTestData
{
    public static async Task<Guid> DefaultIdAsync(NpgsqlDataSource dataSource, string kind, string name)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<Guid>(
            "SELECT id FROM lookup_items WHERE owner_id IS NULL AND kind = @kind AND name = @name", new { kind, name });
    }

    public static async Task<Guid> InsertCustomAsync(NpgsqlDataSource dataSource, string ownerId, string kind, string name)
    {
        var id = Guid.NewGuid();
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO lookup_items (id, kind, owner_id, name, created_at, created_by) VALUES (@id, @kind, @ownerId, @name, now(), @ownerId)",
            new { id, kind, ownerId, name });
        return id;
    }

    public static async Task HideAsync(NpgsqlDataSource dataSource, string ownerId, Guid itemId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO lookup_item_hidden (owner_id, item_id, hidden_at) VALUES (@ownerId, @itemId, now())", new { ownerId, itemId });
    }
}
