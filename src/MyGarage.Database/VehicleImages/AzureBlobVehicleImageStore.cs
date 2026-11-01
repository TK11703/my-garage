using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace MyGarage.Database.VehicleImages;

public sealed class AzureBlobVehicleImageStore(BlobServiceClient service, string containerName = AzureBlobVehicleImageStore.DefaultContainerName)
    : IVehicleImageBlobStore
{
    public const string DefaultContainerName = "vehicle-images";

    private readonly BlobContainerClient container = service.GetBlobContainerClient(containerName);

    // The container is private: no public access type is requested.
    public Task EnsureContainerAsync(CancellationToken cancellationToken) =>
        GuardAsync(() => container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken));

    public Task UploadAsync(Guid vehicleId, Guid imageId, Stream content, string contentType, CancellationToken cancellationToken) =>
        GuardAsync(() => container.GetBlobClient(BlobName(vehicleId, imageId)).UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken));

    public async Task<Stream?> OpenReadAsync(Guid vehicleId, Guid imageId, CancellationToken cancellationToken)
    {
        try
        {
            return await container.GetBlobClient(BlobName(vehicleId, imageId)).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            throw new ImageStorageUnavailableException("Image storage is unavailable.", ex);
        }
    }

    public async Task DeleteAsync(Guid vehicleId, Guid imageId, CancellationToken cancellationToken) =>
        await GuardAsync(() => container.GetBlobClient(BlobName(vehicleId, imageId)).DeleteIfExistsAsync(cancellationToken: cancellationToken));

    public Task DeleteVehicleAsync(Guid vehicleId, CancellationToken cancellationToken) =>
        GuardAsync(async () =>
        {
            await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{vehicleId}/", cancellationToken))
            {
                await container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
            }
        });

    private static string BlobName(Guid vehicleId, Guid imageId) => $"{vehicleId}/{imageId}";

    private static async Task GuardAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            throw new ImageStorageUnavailableException("Image storage is unavailable.", ex);
        }
    }

    private static bool IsStorageFailure(Exception ex) =>
        ex is RequestFailedException or HttpRequestException or IOException or AggregateException;
}
