using Dapper;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.Users;

// Known users, so a sender address can be matched; written only from validated claims.
public sealed class UserProfileRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    public async Task UpsertAsync(string ownerId, string? email, string displayName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Users/Upsert"), new { OwnerId = ownerId, Email = email, DisplayName = displayName, Now = now }, cancellationToken: cancellationToken));
    }

    // At most two owner ids, enough for the caller to tell a unique match from an ambiguous address.
    public async Task<IReadOnlyList<string>> FindOwnersByEmailAsync(string email, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<string>(new CommandDefinition(
            scripts.Get("Users/FindOwnersByEmail"), new { Email = email }, cancellationToken: cancellationToken))).ToList();
    }
}
