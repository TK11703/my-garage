using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.Users;

namespace MyGarage.Database.Tests.Users;

[Collection(DatabaseCollection.Name)]
public class UserProfileRepositoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private async Task<(UserProfileRepository Repository, Npgsql.NpgsqlDataSource DataSource)> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (new UserProfileRepository(dataSource, new SqlScriptStore()), dataSource);
    }

    private sealed record ProfileRow(string OwnerId, string? Email, string DisplayName, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt);

    private static async Task<ProfileRow> ReadAsync(Npgsql.NpgsqlDataSource dataSource, string ownerId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.QuerySingleAsync<ProfileRow>(
            "SELECT owner_id AS OwnerId, email, display_name AS DisplayName, first_seen_at AS FirstSeenAt, last_seen_at AS LastSeenAt FROM user_profiles WHERE owner_id = @ownerId",
            new { ownerId });
    }

    [Fact]
    public async Task Upsert_NewProfile_IsInserted()
    {
        var (repository, dataSource) = await CreateAsync();

        await repository.UpsertAsync("owner-a", "alice@example.com", "Alice", T0, CancellationToken.None);

        var row = await ReadAsync(dataSource, "owner-a");
        Assert.Equal("alice@example.com", row.Email);
        Assert.Equal("Alice", row.DisplayName);
        Assert.Equal(T0, row.FirstSeenAt);
        Assert.Equal(T0, row.LastSeenAt);
    }

    [Fact]
    public async Task Upsert_ExistingProfile_UpdatesEmailNameAndLastSeenButKeepsFirstSeen()
    {
        var (repository, dataSource) = await CreateAsync();
        await repository.UpsertAsync("owner-a", "old@example.com", "Old Name", T0, CancellationToken.None);

        await repository.UpsertAsync("owner-a", "new@example.com", "New Name", T0.AddHours(2), CancellationToken.None);

        var row = await ReadAsync(dataSource, "owner-a");
        Assert.Equal("new@example.com", row.Email);
        Assert.Equal("New Name", row.DisplayName);
        Assert.Equal(T0, row.FirstSeenAt);
        Assert.Equal(T0.AddHours(2), row.LastSeenAt);
    }

    [Fact]
    public async Task Upsert_NullEmail_IsStoredAsNull()
    {
        var (repository, dataSource) = await CreateAsync();

        await repository.UpsertAsync("owner-a", null, "Alice", T0, CancellationToken.None);

        Assert.Null((await ReadAsync(dataSource, "owner-a")).Email);
    }

    [Fact]
    public async Task FindOwnersByEmail_MatchesCaseInsensitively()
    {
        var (repository, dataSource) = await CreateAsync();
        await EmailIntakeTestData.InsertUserProfileAsync(dataSource, "owner-a", "Alice@Example.com");
        await EmailIntakeTestData.InsertUserProfileAsync(dataSource, "owner-b", "bob@example.com");

        var owners = await repository.FindOwnersByEmailAsync("alice@EXAMPLE.COM", CancellationToken.None);

        Assert.Equal(["owner-a"], owners);
    }

    [Fact]
    public async Task FindOwnersByEmail_UnknownAddress_ReturnsNone()
    {
        var (repository, dataSource) = await CreateAsync();
        await EmailIntakeTestData.InsertUserProfileAsync(dataSource, "owner-a", "alice@example.com");
        await EmailIntakeTestData.InsertUserProfileAsync(dataSource, "owner-b", email: null);

        Assert.Empty(await repository.FindOwnersByEmailAsync("nobody@example.com", CancellationToken.None));
    }

    [Fact]
    public async Task FindOwnersByEmail_SharedAddress_ReturnsAtMostTwoRows()
    {
        var (repository, dataSource) = await CreateAsync();
        foreach (var owner in new[] { "owner-a", "owner-b", "owner-c" })
        {
            await EmailIntakeTestData.InsertUserProfileAsync(dataSource, owner, "shared@example.com");
        }

        var owners = await repository.FindOwnersByEmailAsync("shared@example.com", CancellationToken.None);

        Assert.Equal(2, owners.Count);
    }
}
