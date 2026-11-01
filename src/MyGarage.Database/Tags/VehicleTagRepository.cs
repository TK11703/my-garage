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

        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Tags/Insert"), new { Id = tagId, VehicleId = vehicleId, Name = name, CreatedAt = createdAt }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return new AddTagResult(AddTagStatus.Added, new VehicleTagRecord { Id = tagId, Name = name, CreatedAt = createdAt });
    }

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
