using Azure.Storage.Blobs;
using Testcontainers.Azurite;

namespace MyGarage.Database.Tests.Common;

public sealed class AzuriteFixture : IAsyncLifetime
{
    private readonly AzuriteContainer container = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0")
        .WithCommand("--skipApiVersionCheck")
        .Build();

    public BlobServiceClient Service { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        Service = new BlobServiceClient(container.GetConnectionString());
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}
