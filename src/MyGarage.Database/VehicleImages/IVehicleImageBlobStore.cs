namespace MyGarage.Database.VehicleImages;

// Private storage for image files; blobs are addressed by vehicle and image id only.
public interface IVehicleImageBlobStore
{
    Task EnsureContainerAsync(CancellationToken cancellationToken);

    Task UploadAsync(Guid vehicleId, Guid imageId, Stream content, string contentType, CancellationToken cancellationToken);

    // Null when the blob does not exist.
    Task<Stream?> OpenReadAsync(Guid vehicleId, Guid imageId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid vehicleId, Guid imageId, CancellationToken cancellationToken);

    // Removes every blob stored for the vehicle.
    Task DeleteVehicleAsync(Guid vehicleId, CancellationToken cancellationToken);
}
