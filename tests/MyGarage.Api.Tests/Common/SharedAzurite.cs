using Azure.Storage.Blobs;
using Testcontainers.Azurite;

namespace MyGarage.Api.Tests.Common;

// One Azurite container for the whole test run; each test class uses its own blob container inside it.
internal static class SharedAzurite
{
    private static readonly Lazy<Task<AzuriteContainer>> Container = new(async () =>
    {
        var container = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0")
            .WithCommand("--skipApiVersionCheck")
            .Build();
        await container.StartAsync();
        return container;
    });

    public static async Task<BlobServiceClient> CreateServiceClientAsync() =>
        new((await Container.Value).GetConnectionString());
}
