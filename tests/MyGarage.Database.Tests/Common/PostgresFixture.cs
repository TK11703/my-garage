using Npgsql;
using Testcontainers.PostgreSql;

namespace MyGarage.Database.Tests.Common;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithCommand("-c", "max_connections=400")
        .Build();

    public Task InitializeAsync() => container.StartAsync();

    public Task DisposeAsync() => container.DisposeAsync().AsTask();

    // Each call returns a data source for a brand-new empty database inside the shared container.
    public async Task<NpgsqlDataSource> CreateDatabaseAsync()
    {
        var name = $"garage_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await command.ExecuteNonQueryAsync();
        }

        // Every test has its own database and data source, so pooling would keep idle connections open until the server runs out.
        var builder = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name, Pooling = false };
        return NpgsqlDataSource.Create(builder.ConnectionString);
    }
}
