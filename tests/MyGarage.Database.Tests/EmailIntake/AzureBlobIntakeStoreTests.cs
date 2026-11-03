using Azure.Storage.Blobs.Models;
using MyGarage.Database.EmailIntake;
using MyGarage.Database.Tests.Common;

namespace MyGarage.Database.Tests.EmailIntake;

[Collection(DatabaseCollection.Name)]
public class AzureBlobIntakeStoreTests(AzuriteFixture azurite)
{
    private async Task<(AzureBlobIntakeStore Store, string ContainerName)> CreateAsync()
    {
        var containerName = $"intake-{Guid.NewGuid():N}";
        var store = new AzureBlobIntakeStore(azurite.Service, containerName);
        await store.EnsureContainerAsync(CancellationToken.None);
        return (store, containerName);
    }

    private static async Task<List<string>> BlobNamesAsync(AzuriteFixture azurite, string containerName)
    {
        var names = new List<string>();
        await foreach (var blob in azurite.Service.GetBlobContainerClient(containerName).GetBlobsAsync())
        {
            names.Add(blob.Name);
        }

        return names;
    }

    [Fact]
    public void DefaultContainerName_IsEmailIntake() =>
        Assert.Equal("email-intake", AzureBlobIntakeStore.DefaultContainerName);

    [Fact]
    public async Task EnsureContainer_CreatesAPrivateContainer()
    {
        var (_, containerName) = await CreateAsync();

        var properties = (await azurite.Service.GetBlobContainerClient(containerName).GetPropertiesAsync()).Value;

        Assert.Equal(PublicAccessType.None, properties.PublicAccess ?? PublicAccessType.None);
    }

    [Fact]
    public async Task EnsureContainer_CalledTwice_Succeeds()
    {
        var (store, _) = await CreateAsync();

        await store.EnsureContainerAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Upload_ThenOpenRead_ReturnsTheBytesUnderSubmissionSlashAttachment()
    {
        var (store, containerName) = await CreateAsync();
        var submissionId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();

        await store.UploadAsync(submissionId, attachmentId, new MemoryStream([1, 2, 3, 4]), "application/pdf", CancellationToken.None);

        Assert.Equal([$"{submissionId}/{attachmentId}"], await BlobNamesAsync(azurite, containerName));
        await using var stream = await store.OpenReadAsync(submissionId, attachmentId, CancellationToken.None);
        Assert.NotNull(stream);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        Assert.Equal([1, 2, 3, 4], buffer.ToArray());
        var properties = (await azurite.Service.GetBlobContainerClient(containerName).GetBlobClient($"{submissionId}/{attachmentId}").GetPropertiesAsync()).Value;
        Assert.Equal("application/pdf", properties.ContentType);
    }

    [Fact]
    public async Task OpenRead_MissingBlob_ReturnsNull()
    {
        var (store, _) = await CreateAsync();

        Assert.Null(await store.OpenReadAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesTheBlobAndIsIdempotent()
    {
        var (store, containerName) = await CreateAsync();
        var submissionId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        await store.UploadAsync(submissionId, attachmentId, new MemoryStream([9]), "image/png", CancellationToken.None);

        await store.DeleteAsync(submissionId, attachmentId, CancellationToken.None);
        await store.DeleteAsync(submissionId, attachmentId, CancellationToken.None);

        Assert.Empty(await BlobNamesAsync(azurite, containerName));
    }

    [Fact]
    public async Task DeleteSubmission_RemovesEveryBlobOfThatSubmissionOnly()
    {
        var (store, containerName) = await CreateAsync();
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        var keep = Guid.NewGuid();
        await store.UploadAsync(mine, Guid.NewGuid(), new MemoryStream([1]), "image/png", CancellationToken.None);
        await store.UploadAsync(mine, Guid.NewGuid(), new MemoryStream([2]), "image/png", CancellationToken.None);
        await store.UploadAsync(other, keep, new MemoryStream([3]), "image/png", CancellationToken.None);

        await store.DeleteSubmissionAsync(mine, CancellationToken.None);

        Assert.Equal([$"{other}/{keep}"], await BlobNamesAsync(azurite, containerName));
    }

    [Fact]
    public async Task ListBlobNamesOlderThan_ReturnsOnlyBlobsLastModifiedBeforeTheCutoff()
    {
        var (store, containerName) = await CreateAsync();
        var submissionId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await store.UploadAsync(submissionId, first, new MemoryStream([1]), "image/png", CancellationToken.None);
        await store.UploadAsync(submissionId, second, new MemoryStream([2]), "image/png", CancellationToken.None);

        // The storage emulator stamps blobs with its own clock, so the cutoffs are taken from the stamps.
        var container = azurite.Service.GetBlobContainerClient(containerName);
        var stamps = new List<DateTimeOffset>();
        foreach (var id in new[] { first, second })
        {
            stamps.Add((await container.GetBlobClient($"{submissionId}/{id}").GetPropertiesAsync()).Value.LastModified);
        }

        var after = await store.ListBlobNamesOlderThanAsync(stamps.Max().AddMinutes(1), CancellationToken.None);
        var before = await store.ListBlobNamesOlderThanAsync(stamps.Min().AddMinutes(-1), CancellationToken.None);

        Assert.Equal(new[] { $"{submissionId}/{first}", $"{submissionId}/{second}" }.Order(), after.Order());
        Assert.Empty(before);
    }

    [Fact]
    public async Task ListBlobNamesOlderThan_EmptyContainer_ReturnsNothing()
    {
        var (store, _) = await CreateAsync();

        Assert.Empty(await store.ListBlobNamesOlderThanAsync(DateTimeOffset.UtcNow.AddDays(1), CancellationToken.None));
    }

    [Fact]
    public async Task UnreachableStorage_RaisesIntakeStorageUnavailableException()
    {
        var unreachable = new Azure.Storage.Blobs.BlobServiceClient(
            "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:1/devstoreaccount1;",
            new Azure.Storage.Blobs.BlobClientOptions { Retry = { MaxRetries = 0, NetworkTimeout = TimeSpan.FromSeconds(3) } });
        var store = new AzureBlobIntakeStore(unreachable, "email-intake");

        await Assert.ThrowsAsync<IntakeStorageUnavailableException>(() => store.EnsureContainerAsync(CancellationToken.None));
        await Assert.ThrowsAsync<IntakeStorageUnavailableException>(() =>
            store.UploadAsync(Guid.NewGuid(), Guid.NewGuid(), new MemoryStream([1]), "image/png", CancellationToken.None));
        await Assert.ThrowsAsync<IntakeStorageUnavailableException>(() =>
            store.ListBlobNamesOlderThanAsync(DateTimeOffset.UtcNow, CancellationToken.None));
    }
}
