using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using MyGarage.Database.EmailIntake;

namespace MyGarage.Database.VehicleDocuments;

public sealed class AzureBlobVehicleDocumentStore(
    BlobServiceClient service,
    string containerName = AzureBlobVehicleDocumentStore.DefaultContainerName,
    string intakeContainerName = AzureBlobIntakeStore.DefaultContainerName) : IVehicleDocumentBlobStore
{
    public const string DefaultContainerName = "vehicle-documents";

    private readonly BlobContainerClient container = service.GetBlobContainerClient(containerName);
    private readonly BlobContainerClient intake = service.GetBlobContainerClient(intakeContainerName);

    // The container is private: no public access type is requested.
    public Task EnsureContainerAsync(CancellationToken cancellationToken) =>
        GuardAsync(() => container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken));

    public async Task<bool> CopyFromIntakeAsync(Guid submissionId, Guid attachmentId, Guid vehicleId, Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            var source = intake.GetBlobClient($"{submissionId}/{attachmentId}");
            if (!await source.ExistsAsync(cancellationToken))
            {
                return false;
            }

            var copy = await container.GetBlobClient(BlobName(vehicleId, documentId)).StartCopyFromUriAsync(source.Uri, cancellationToken: cancellationToken);
            await copy.WaitForCompletionAsync(cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            throw new DocumentStorageUnavailableException("Document storage is unavailable.", ex);
        }
    }

    public async Task<Stream?> OpenReadAsync(Guid vehicleId, Guid documentId, CancellationToken cancellationToken)
    {
        try
        {
            return await container.GetBlobClient(BlobName(vehicleId, documentId)).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            throw new DocumentStorageUnavailableException("Document storage is unavailable.", ex);
        }
    }

    public async Task DeleteAsync(Guid vehicleId, Guid documentId, CancellationToken cancellationToken) =>
        await GuardAsync(() => container.GetBlobClient(BlobName(vehicleId, documentId)).DeleteIfExistsAsync(cancellationToken: cancellationToken));

    public Task DeleteVehicleAsync(Guid vehicleId, CancellationToken cancellationToken) =>
        GuardAsync(async () =>
        {
            await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{vehicleId}/", cancellationToken))
            {
                await container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
            }
        });

    private static string BlobName(Guid vehicleId, Guid documentId) => $"{vehicleId}/{documentId}";

    private static async Task GuardAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            throw new DocumentStorageUnavailableException("Document storage is unavailable.", ex);
        }
    }

    private static bool IsStorageFailure(Exception ex) =>
        ex is RequestFailedException or HttpRequestException or IOException or AggregateException;
}
