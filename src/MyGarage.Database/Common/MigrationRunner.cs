using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MyGarage.Database.Common;

// Applies numbered, forward-only scripts and records each in the schema_migrations ledger with its checksum.
public sealed class MigrationRunner(NpgsqlDataSource dataSource, SqlScriptStore scripts, ILogger<MigrationRunner> logger)
{
    private const string MigrationsFolder = "Migrations";

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        // The advisory lock serialises concurrent starts of the application.
        await connection.ExecuteAsync(new CommandDefinition(scripts.Get("Common/AcquireMigrationLock"), cancellationToken: cancellationToken));
        try
        {
            await ApplyPendingAsync(connection, cancellationToken);
        }
        finally
        {
            await connection.ExecuteAsync(new CommandDefinition(scripts.Get("Common/ReleaseMigrationLock"), cancellationToken: CancellationToken.None));
        }
    }

    private async Task ApplyPendingAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(scripts.Get("Common/EnsureMigrationLedger"), cancellationToken: cancellationToken));

        var applied = (await connection.QueryAsync<AppliedMigration>(
                new CommandDefinition(scripts.Get("Common/GetAppliedMigrations"), cancellationToken: cancellationToken)))
            .ToDictionary(migration => migration.ScriptId, migration => migration.Checksum, StringComparer.Ordinal);

        foreach (var scriptId in scripts.List(MigrationsFolder))
        {
            var text = scripts.Get($"{MigrationsFolder}/{scriptId}");
            var checksum = ComputeChecksum(text);

            if (applied.TryGetValue(scriptId, out var recorded))
            {
                if (!string.Equals(recorded, checksum, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Migration '{scriptId}' was changed after it was applied. Applied migrations must not be edited; add a new migration instead.");
                }

                continue;
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await connection.ExecuteAsync(new CommandDefinition(text, transaction: transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                scripts.Get("Common/RecordMigration"),
                new { ScriptId = scriptId, Checksum = checksum, AppliedAt = DateTimeOffset.UtcNow },
                transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Applied migration {ScriptId}", scriptId);
        }
    }

    // Line endings are normalised so a checkout with different git autocrlf settings yields the same checksum.
    private static string ComputeChecksum(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal))));

    private sealed record AppliedMigration
    {
        public string ScriptId { get; init; } = string.Empty;

        public string Checksum { get; init; } = string.Empty;
    }
}
