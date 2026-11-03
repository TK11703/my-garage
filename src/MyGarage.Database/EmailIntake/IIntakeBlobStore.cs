namespace MyGarage.Database.EmailIntake;

// Private storage for email attachments waiting for review; blobs are addressed by submission and attachment id only.
public interface IIntakeBlobStore
{
    Task EnsureContainerAsync(CancellationToken cancellationToken);

    Task UploadAsync(Guid submissionId, Guid attachmentId, Stream content, string contentType, CancellationToken cancellationToken);

    // Null when the blob does not exist.
    Task<Stream?> OpenReadAsync(Guid submissionId, Guid attachmentId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid submissionId, Guid attachmentId, CancellationToken cancellationToken);

    // Removes every blob stored for the submission.
    Task DeleteSubmissionAsync(Guid submissionId, CancellationToken cancellationToken);

    // Names ({submissionId}/{attachmentId}) of the blobs last modified before the cutoff, for the orphan sweep.
    Task<IReadOnlyList<string>> ListBlobNamesOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}
