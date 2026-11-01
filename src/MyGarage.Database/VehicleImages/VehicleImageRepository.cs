using Dapper;
using Microsoft.Extensions.Logging;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.VehicleImages;

// Keeps the image rows (PostgreSQL) and image files (blob storage) consistent. Every statement is owner-scoped
// through the parent vehicle, and a blob is only read, written, or deleted after that check has passed.
public sealed class VehicleImageRepository(
    NpgsqlDataSource dataSource,
    SqlScriptStore scripts,
    IVehicleImageBlobStore blobs,
    ILogger<VehicleImageRepository> logger)
{
    public const int MaxImagesPerVehicle = 10;

    // The row and the blob are written in one transaction: if the upload fails nothing is stored, and the first image becomes main.
    public async Task<AddImageResult> AddAsync(
        string ownerId,
        Guid vehicleId,
        Guid imageId,
        string contentType,
        int sizeBytes,
        string? caption,
        Stream content,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await LockVehicleAsync(connection, transaction, ownerId, vehicleId, cancellationToken))
        {
            return new AddImageResult(AddImageStatus.VehicleNotFound);
        }

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            scripts.Get("VehicleImages/CountForVehicle"), new { VehicleId = vehicleId }, transaction, cancellationToken: cancellationToken));
        if (count >= MaxImagesPerVehicle)
        {
            return new AddImageResult(AddImageStatus.LimitReached);
        }

        var image = new VehicleImageRecord
        {
            Id = imageId,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Caption = caption,
            IsMain = count == 0,
            CreatedAt = now,
        };

        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("VehicleImages/Insert"),
            new
            {
                Id = imageId,
                VehicleId = vehicleId,
                ContentType = contentType,
                SizeBytes = sizeBytes,
                Caption = caption,
                image.IsMain,
                CreatedAt = now,
                CreatedBy = ownerId,
            },
            transaction,
            cancellationToken: cancellationToken));

        await blobs.UploadAsync(vehicleId, imageId, content, contentType, cancellationToken);

        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await TryDeleteBlobAsync(vehicleId, imageId);
            throw;
        }

        return new AddImageResult(AddImageStatus.Added, image);
    }

    public async Task<IReadOnlyList<VehicleImageRecord>> ListAsync(string ownerId, Guid vehicleId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return (await connection.QueryAsync<VehicleImageRecord>(new CommandDefinition(
            scripts.Get("VehicleImages/ListByVehicle"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken))).ToList();
    }

    // Null when the image does not exist for that owner (or its file is missing); the metadata check always comes first.
    public async Task<VehicleImageContent?> OpenContentAsync(string ownerId, Guid vehicleId, Guid imageId, CancellationToken cancellationToken)
    {
        VehicleImageRecord? image;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        {
            image = await connection.QuerySingleOrDefaultAsync<VehicleImageRecord>(new CommandDefinition(
                scripts.Get("VehicleImages/GetById"), new { ImageId = imageId, VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));
        }

        if (image is null)
        {
            return null;
        }

        var stream = await blobs.OpenReadAsync(vehicleId, imageId, cancellationToken);
        return stream is null ? null : new VehicleImageContent(image.ContentType, stream, image.SizeBytes);
    }

    // The previous main image is cleared and the new one set in one transaction.
    public async Task<bool> SetMainAsync(string ownerId, Guid vehicleId, Guid imageId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await LockVehicleAsync(connection, transaction, ownerId, vehicleId, cancellationToken))
        {
            return false;
        }

        var image = await connection.QuerySingleOrDefaultAsync<VehicleImageRecord>(new CommandDefinition(
            scripts.Get("VehicleImages/GetById"), new { ImageId = imageId, VehicleId = vehicleId, OwnerId = ownerId }, transaction, cancellationToken: cancellationToken));
        if (image is null)
        {
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("VehicleImages/ClearMain"), new { VehicleId = vehicleId }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("VehicleImages/SetMain"), new { ImageId = imageId, VehicleId = vehicleId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    // The row goes first (promoting the oldest remaining image if it was main); a failed blob delete only leaves a private orphan.
    public async Task<bool> DeleteAsync(string ownerId, Guid vehicleId, Guid imageId, CancellationToken cancellationToken)
    {
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            if (!await LockVehicleAsync(connection, transaction, ownerId, vehicleId, cancellationToken))
            {
                return false;
            }

            var wasMain = await connection.QuerySingleOrDefaultAsync<bool?>(new CommandDefinition(
                scripts.Get("VehicleImages/Delete"), new { ImageId = imageId, VehicleId = vehicleId, OwnerId = ownerId }, transaction, cancellationToken: cancellationToken));
            if (wasMain is null)
            {
                return false;
            }

            if (wasMain == true)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    scripts.Get("VehicleImages/PromoteOldest"), new { VehicleId = vehicleId }, transaction, cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
        }

        await TryDeleteBlobAsync(vehicleId, imageId);
        return true;
    }

    // Called after a vehicle (and with it its image rows) has been deleted.
    public async Task DeleteBlobsForVehicleAsync(Guid vehicleId)
    {
        try
        {
            await blobs.DeleteVehicleAsync(vehicleId, CancellationToken.None);
        }
        catch (ImageStorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not delete the image files of vehicle {VehicleId}", vehicleId);
        }
    }

    private async Task<bool> LockVehicleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string ownerId, Guid vehicleId, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            scripts.Get("VehicleImages/LockVehicle"), new { VehicleId = vehicleId, OwnerId = ownerId }, transaction, cancellationToken: cancellationToken)) is not null;

    private async Task TryDeleteBlobAsync(Guid vehicleId, Guid imageId)
    {
        try
        {
            await blobs.DeleteAsync(vehicleId, imageId, CancellationToken.None);
        }
        catch (ImageStorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not delete the image file {ImageId} of vehicle {VehicleId}", imageId, vehicleId);
        }
    }
}
