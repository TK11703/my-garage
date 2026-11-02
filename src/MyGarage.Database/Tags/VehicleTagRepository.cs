using Dapper;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.Tags;

public sealed class VehicleTagRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    public const int MaxTagsPerVehicle = 20;

    // The vehicle row is locked (owner-scoped) so the limit and uniqueness checks cannot race with another add.
    public async Task<AddTagResult> AddAsync(
        string ownerId,
        Guid vehicleId,
        Guid tagId,
        string name,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var locked = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            scripts.Get("Tags/LockVehicle"), new { VehicleId = vehicleId, OwnerId = ownerId }, transaction, cancellationToken: cancellationToken));
        if (locked is null)
        {
            return new AddTagResult(AddTagStatus.VehicleNotFound);
        }

        var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            scripts.Get("Tags/Exists"), new { VehicleId = vehicleId, Name = name }, transaction, cancellationToken: cancellationToken));
        if (exists)
        {
            return new AddTagResult(AddTagStatus.Duplicate);
        }

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            scripts.Get("Tags/CountForVehicle"), new { VehicleId = vehicleId }, transaction, cancellationToken: cancellationToken));
        if (count >= MaxTagsPerVehicle)
        {
            return new AddTagResult(AddTagStatus.LimitReached);
        }

        // Reuse the shared feature when another vehicle already has it; otherwise this creates it.
        var feature = await connection.QuerySingleAsync<FeatureRow>(new CommandDefinition(
            scripts.Get("Tags/UpsertFeature"),
            new { Id = Guid.CreateVersion7(), Name = name, CreatedAt = createdAt, OwnerId = ownerId },
            transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Tags/Insert"), new { Id = tagId, VehicleId = vehicleId, FeatureId = feature.Id, CreatedAt = createdAt }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return new AddTagResult(AddTagStatus.Added, new VehicleTagRecord { Id = tagId, Name = feature.Name, CreatedAt = createdAt });
    }

    // Names of shared features that at least one vehicle uses, most used first.
    public async Task<IReadOnlyList<string>> SuggestAsync(string? text, int limit, CancellationToken cancellationToken)
    {
        if (LikePattern.Contains(text) is not { } pattern)
        {
            return [];
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<string>(new CommandDefinition(
            scripts.Get("Tags/SuggestFeatures"), new { Pattern = pattern, Limit = limit }, cancellationToken: cancellationToken))).ToList();
    }

    private sealed record FeatureRow(Guid Id, string Name);

    public async Task<bool> RemoveAsync(string ownerId, Guid vehicleId, Guid tagId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Tags/Delete"), new { TagId = tagId, VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));
        return rows > 0;
    }

    public async Task<IReadOnlyList<VehicleTagRecord>> ListAsync(string ownerId, Guid vehicleId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<VehicleTagRecord>(new CommandDefinition(
            scripts.Get("Tags/ListByVehicle"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken))).ToList();
    }
}
