using MyGarage.Database.EmailIntake;

namespace MyGarage.Api.EmailIntake;

// Makes sure the private blob container exists; a storage outage at startup must not stop the rest of the API.
public sealed class IntakeContainersHostedService(IIntakeBlobStore store, ILogger<IntakeContainersHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.EnsureContainerAsync(cancellationToken);
        }
        catch (IntakeStorageUnavailableException ex)
        {
            logger.LogWarning(ex, "The email intake container could not be prepared; intake will fail until storage is reachable");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
