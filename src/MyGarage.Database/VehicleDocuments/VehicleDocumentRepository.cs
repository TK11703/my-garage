using Dapper;
using Microsoft.Extensions.Logging;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.VehicleDocuments;

// Keeps the document rows (PostgreSQL) and files (blob storage) consistent. Every statement proves ownership through the parent vehicle,
// and a blob is only read or deleted after that check has passed.
public sealed class VehicleDocumentRepository(
    NpgsqlDataSource dataSource,
    SqlScriptStore scripts,
    IVehicleDocumentBlobStore blobs,
    ILogger<VehicleDocumentRepository> logger)
{
    // Newest first. Null when the vehicle does not exist for that owner; a page past the last is empty.
    public async Task<PagedResult<VehicleDocumentRecord>?> ListPageAsync(
        string ownerId, Guid vehicleId, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var owns = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            scripts.Get("VehicleDocuments/VehicleExists"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));
        if (!owns)
        {
            return null;
        }

        var items = (await connection.QueryAsync<VehicleDocumentRecord>(new CommandDefinition(
            scripts.Get("VehicleDocuments/ListPage"),
            new { VehicleId = vehicleId, OwnerId = ownerId, PageSize = pageSize, Offset = (page - 1) * pageSize },
            cancellationToken: cancellationToken))).ToList();
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            scripts.Get("VehicleDocuments/Count"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));

        return new PagedResult<VehicleDocumentRecord>(items, page, pageSize, total);
    }

    // Null when the document does not exist for that owner and vehicle (or its file is missing); the metadata check always comes first.
    public async Task<VehicleDocumentContent?> OpenContentAsync(string ownerId, Guid vehicleId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await GetAsync(ownerId, vehicleId, documentId, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var stream = await blobs.OpenReadAsync(vehicleId, documentId, cancellationToken);
        return stream is null ? null : new VehicleDocumentContent(document.ContentType, document.FileName, stream, document.SizeBytes);
    }

    // The file goes first (a storage failure leaves the row so the user can retry), then the row. False when the document is not the owner's.
    public async Task<bool> DeleteAsync(string ownerId, Guid vehicleId, Guid documentId, CancellationToken cancellationToken)
    {
        if (await GetAsync(ownerId, vehicleId, documentId, cancellationToken) is null)
        {
            return false;
        }

        await blobs.DeleteAsync(vehicleId, documentId, cancellationToken);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("VehicleDocuments/Delete"),
            new { DocumentId = documentId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken));
        return true;
    }

    // Called after a vehicle (and with it its document rows) has been deleted; a failure only leaves private orphans.
    public async Task DeleteBlobsForVehicleAsync(Guid vehicleId)
    {
        try
        {
            await blobs.DeleteVehicleAsync(vehicleId, CancellationToken.None);
        }
        catch (DocumentStorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not delete the document files of vehicle {VehicleId}", vehicleId);
        }
    }

    private async Task<VehicleDocumentRecord?> GetAsync(string ownerId, Guid vehicleId, Guid documentId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<VehicleDocumentRecord>(new CommandDefinition(
            scripts.Get("VehicleDocuments/GetById"),
            new { DocumentId = documentId, VehicleId = vehicleId, OwnerId = ownerId },
            cancellationToken: cancellationToken));
    }
}
