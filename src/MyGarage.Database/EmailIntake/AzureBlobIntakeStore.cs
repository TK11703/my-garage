using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace MyGarage.Database.EmailIntake;

public sealed class AzureBlobIntakeStore(BlobServiceClient service, string containerName = AzureBlobIntakeStore.DefaultContainerName)
    : IIntakeBlobStore
{
    public const string DefaultContainerName = "email-intake";

    private readonly BlobContainerClient container = service.GetBlobContainerClient(containerName);

    // The container is private: no public access type is requested.
    public Task EnsureContainerAsync(CancellationToken cancellationToken) =>
        GuardAsync(() => container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken));

    public Task UploadAsync(Guid submissionId, Guid attachmentId, Stream content, string contentType, CancellationToken cancellationToken) =>
        GuardAsync(() => container.GetBlobClient(BlobName(submissionId, attachmentId)).UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken));

    public async Task<Stream?> OpenReadAsync(Guid submissionId, Guid attachmentId, CancellationToken cancellationToken)
    {
        try
        {
            return await container.GetBlobClient(BlobName(submissionId, attachmentId)).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            throw new IntakeStorageUnavailableException("Email attachment storage is unavailable.", ex);
        }
    }

    public async Task DeleteAsync(Guid submissionId, Guid attachmentId, CancellationToken cancellationToken) =>
        await GuardAsync(() => container.GetBlobClient(BlobName(submissionId, attachmentId)).DeleteIfExistsAsync(cancellationToken: cancellationToken));

    public Task DeleteSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) =>
        GuardAsync(async () =>
        {
            await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{submissionId}/", cancellationToken))
            {
                await container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
            }
        });

    private static string BlobName(Guid submissionId, Guid attachmentId) => $"{submissionId}/{attachmentId}";

    public async Task<IReadOnlyList<string>> ListBlobNamesOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        await GuardAsync(async () =>
        {
            await foreach (var blob in container.GetBlobsAsync(cancellationToken: cancellationToken))
            {
                if (blob.Properties.LastModified is { } modified && modified < cutoff)
                {
                    names.Add(blob.Name);
                }
            }
        });
        return names;
    }

    private static async Task GuardAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            throw new IntakeStorageUnavailableException("Email attachment storage is unavailable.", ex);
        }
    }

    private static bool IsStorageFailure(Exception ex) =>
        ex is RequestFailedException or HttpRequestException or IOException or AggregateException;
}
