using MyGarage.Database.VehicleImages;

namespace MyGarage.Api.Vehicles.Images;

// Makes sure the private blob container exists; a storage outage at startup must not stop the rest of the API.
public sealed class ImageContainerHostedService(IVehicleImageBlobStore store, ILogger<ImageContainerHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.EnsureContainerAsync(cancellationToken);
        }
        catch (ImageStorageUnavailableException ex)
        {
            logger.LogWarning(ex, "The image container could not be prepared; image features will fail until storage is reachable");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
