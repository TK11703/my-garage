using Npgsql;
using Testcontainers.PostgreSql;

namespace MyGarage.Api.Tests.Common;

// One PostgreSQL container for the whole test run; each test class gets its own empty database inside it.
internal static class SharedPostgres
{
    private static readonly Lazy<Task<PostgreSqlContainer>> Container = new(async () =>
    {
        var container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithCommand("-c", "max_connections=400")
            .Build();
        await container.StartAsync();
        return container;
    });

    public static async Task<string> CreateDatabaseAsync()
    {
        var container = await Container.Value;
        var name = $"garage_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}
