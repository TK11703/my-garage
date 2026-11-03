namespace MyGarage.Database.VehicleDocuments;

// Private storage for kept originals; blobs are addressed by vehicle and document id only.
public interface IVehicleDocumentBlobStore
{
    Task EnsureContainerAsync(CancellationToken cancellationToken);

    // Copies an email-intake blob into the documents container on the storage side; false when the source blob does not exist.
    Task<bool> CopyFromIntakeAsync(Guid submissionId, Guid attachmentId, Guid vehicleId, Guid documentId, CancellationToken cancellationToken);

    // Null when the blob does not exist.
    Task<Stream?> OpenReadAsync(Guid vehicleId, Guid documentId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid vehicleId, Guid documentId, CancellationToken cancellationToken);

    // Removes every blob stored for the vehicle.
    Task DeleteVehicleAsync(Guid vehicleId, CancellationToken cancellationToken);
}
