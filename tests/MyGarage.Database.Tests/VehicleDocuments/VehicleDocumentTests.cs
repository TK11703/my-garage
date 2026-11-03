using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using MyGarage.Database.Common;
using MyGarage.Database.EmailIntake;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.VehicleDocuments;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.VehicleDocuments;

[Collection(DatabaseCollection.Name)]
public class VehicleDocumentRepositoryTests(PostgresFixture postgres, AzuriteFixture azurite)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required NpgsqlDataSource DataSource { get; init; }

        public required VehicleRepository Vehicles { get; init; }

        public required VehicleDocumentRepository Documents { get; init; }

        public required BlobContainerClient Container { get; init; }

        public required FailingStore Store { get; init; }
    }

    // Wraps the real store so a test can make storage fail.
    private sealed class FailingStore(IVehicleDocumentBlobStore inner) : IVehicleDocumentBlobStore
    {
        public bool Fail { get; set; }

        public Task EnsureContainerAsync(CancellationToken cancellationToken) => inner.EnsureContainerAsync(cancellationToken);

        public Task<bool> CopyFromIntakeAsync(Guid submissionId, Guid attachmentId, Guid vehicleId, Guid documentId, CancellationToken cancellationToken) =>
            Fail ? throw new DocumentStorageUnavailableException("injected") : inner.CopyFromIntakeAsync(submissionId, attachmentId, vehicleId, documentId, cancellationToken);

        public Task<Stream?> OpenReadAsync(Guid vehicleId, Guid documentId, CancellationToken cancellationToken) =>
            Fail ? throw new DocumentStorageUnavailableException("injected") : inner.OpenReadAsync(vehicleId, documentId, cancellationToken);

        public Task DeleteAsync(Guid vehicleId, Guid documentId, CancellationToken cancellationToken) =>
            Fail ? throw new DocumentStorageUnavailableException("injected") : inner.DeleteAsync(vehicleId, documentId, cancellationToken);

        public Task DeleteVehicleAsync(Guid vehicleId, CancellationToken cancellationToken) =>
            Fail ? throw new DocumentStorageUnavailableException("injected") : inner.DeleteVehicleAsync(vehicleId, cancellationToken);
    }

    private async Task<Harness> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var containerName = $"docs-{Guid.NewGuid():N}";
        var store = new AzureBlobVehicleDocumentStore(azurite.Service, containerName);
        await store.EnsureContainerAsync(CancellationToken.None);
        var failing = new FailingStore(store);
        var scripts = new SqlScriptStore();
        return new Harness
        {
            DataSource = dataSource,
            Vehicles = new VehicleRepository(dataSource, scripts),
            Documents = new VehicleDocumentRepository(dataSource, scripts, failing, NullLogger<VehicleDocumentRepository>.Instance),
            Container = azurite.Service.GetBlobContainerClient(containerName),
            Store = failing,
        };
    }

    private static async Task<VehicleRecord> InsertVehicleAsync(Harness harness, string ownerId)
    {
        var vehicle = TestData.Vehicle(ownerId);
        await harness.Vehicles.InsertAsync(vehicle, CancellationToken.None);
        return vehicle;
    }

    // Seeds the row and the blob together.
    private static async Task<Guid> SeedDocumentAsync(
        Harness harness, Guid vehicleId, byte[]? bytes = null, string contentType = "application/pdf", string fileName = "invoice.pdf", DateTimeOffset? at = null)
    {
        var data = bytes ?? EmailIntakeTestData.PdfBytes;
        var id = await EmailIntakeTestData.InsertVehicleDocumentAsync(
            harness.DataSource, vehicleId, fileName: fileName, contentType: contentType, sizeBytes: data.Length, createdAt: at ?? T0, sourceKind: "service_record");
        await harness.Container.GetBlobClient($"{vehicleId}/{id}").UploadAsync(new BinaryData(data));
        return id;
    }

    private static async Task<bool> BlobExistsAsync(Harness harness, Guid vehicleId, Guid documentId) =>
        await harness.Container.GetBlobClient($"{vehicleId}/{documentId}").ExistsAsync();

    [Fact]
    public async Task ListPage_ReturnsTheVehiclesDocumentsNewestFirst_AndPages()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-a");
        var oldest = await SeedDocumentAsync(harness, vehicle.Id, at: T0.AddDays(-2));
        var middle = await SeedDocumentAsync(harness, vehicle.Id, at: T0.AddDays(-1));
        var newest = await SeedDocumentAsync(harness, vehicle.Id, at: T0);
        var other = await InsertVehicleAsync(harness, "owner-a");
        await SeedDocumentAsync(harness, other.Id);

        var first = await harness.Documents.ListPageAsync("owner-a", vehicle.Id, 1, 2, CancellationToken.None);
        var second = await harness.Documents.ListPageAsync("owner-a", vehicle.Id, 2, 2, CancellationToken.None);
        var beyond = await harness.Documents.ListPageAsync("owner-a", vehicle.Id, 3, 2, CancellationToken.None);

        Assert.Equal([newest, middle], first!.Items.Select(item => item.Id));
        Assert.Equal([oldest], second!.Items.Select(item => item.Id));
        Assert.Empty(beyond!.Items);
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal("invoice.pdf", first.Items[0].FileName);
        Assert.Equal("application/pdf", first.Items[0].ContentType);
        Assert.Equal("service_record", first.Items[0].SourceKind);
        Assert.Equal(EmailIntakeTestData.PdfBytes.Length, first.Items[0].SizeBytes);
    }

    [Fact]
    public async Task ListPage_ForAVehicleThatIsNotTheOwners_ReturnsNull()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-a");
        await SeedDocumentAsync(harness, vehicle.Id);

        Assert.Null(await harness.Documents.ListPageAsync("owner-b", vehicle.Id, 1, 10, CancellationToken.None));
        Assert.Null(await harness.Documents.ListPageAsync("owner-a", Guid.NewGuid(), 1, 10, CancellationToken.None));
    }

    [Fact]
    public async Task OpenContent_ReturnsTheBytesAndContentTypeOnlyForTheOwner()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-a");
        var id = await SeedDocumentAsync(harness, vehicle.Id, EmailIntakeTestData.PngBytes, "image/png", "scan.png");

        await using (var content = await harness.Documents.OpenContentAsync("owner-a", vehicle.Id, id, CancellationToken.None))
        {
            Assert.NotNull(content);
            Assert.Equal("image/png", content.ContentType);
            Assert.Equal("scan.png", content.FileName);
            using var buffer = new MemoryStream();
            await content.Content.CopyToAsync(buffer);
            Assert.Equal(EmailIntakeTestData.PngBytes, buffer.ToArray());
        }

        Assert.Null(await harness.Documents.OpenContentAsync("owner-b", vehicle.Id, id, CancellationToken.None));
        var otherVehicle = await InsertVehicleAsync(harness, "owner-a");
        Assert.Null(await harness.Documents.OpenContentAsync("owner-a", otherVehicle.Id, id, CancellationToken.None));
        Assert.Null(await harness.Documents.OpenContentAsync("owner-a", vehicle.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task OpenContent_WhenTheFileIsMissing_ReturnsNull()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-a");
        var id = await EmailIntakeTestData.InsertVehicleDocumentAsync(harness.DataSource, vehicle.Id);

        Assert.Null(await harness.Documents.OpenContentAsync("owner-a", vehicle.Id, id, CancellationToken.None));
    }

    [Fact]
    public async Task OpenContent_WhenStorageFails_Throws()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-a");
        var id = await SeedDocumentAsync(harness, vehicle.Id);
        harness.Store.Fail = true;

        await Assert.ThrowsAsync<DocumentStorageUnavailableException>(() => harness.Documents.OpenContentAsync("owner-a", vehicle.Id, id, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesTheRowAndTheBlob()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-a");
        var id = await SeedDocumentAsync(harness, vehicle.Id);

        Assert.True(await harness.Documents.DeleteAsync("owner-a", vehicle.Id, id, CancellationToken.None));

        Assert.False(await BlobExistsAsync(harness, vehicle.Id, id));
        Assert.Empty((await harness.Documents.ListPageAsync("owner-a", vehicle.Id, 1, 10, CancellationToken.None))!.Items);
        Assert.False(await harness.Documents.DeleteAsync("owner-a", vehicle.Id, id, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_ForAnotherOwnerOrAnotherVehicle_ReturnsFalseAndKeepsTheDocument()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-a");
        var otherVehicle = await InsertVehicleAsync(harness, "owner-a");
        var id = await SeedDocumentAsync(harness, vehicle.Id);

        Assert.False(await harness.Documents.DeleteAsync("owner-b", vehicle.Id, id, CancellationToken.None));
        Assert.False(await harness.Documents.DeleteAsync("owner-a", otherVehicle.Id, id, CancellationToken.None));

        Assert.True(await BlobExistsAsync(harness, vehicle.Id, id));
        Assert.Single((await harness.Documents.ListPageAsync("owner-a", vehicle.Id, 1, 10, CancellationToken.None))!.Items);
    }

    [Fact]
    public async Task Delete_WhenStorageFails_KeepsTheRowSoTheUserCanRetry()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-a");
        var id = await SeedDocumentAsync(harness, vehicle.Id);
        harness.Store.Fail = true;

        await Assert.ThrowsAsync<DocumentStorageUnavailableException>(() => harness.Documents.DeleteAsync("owner-a", vehicle.Id, id, CancellationToken.None));

        harness.Store.Fail = false;
        Assert.Single((await harness.Documents.ListPageAsync("owner-a", vehicle.Id, 1, 10, CancellationToken.None))!.Items);
        Assert.True(await harness.Documents.DeleteAsync("owner-a", vehicle.Id, id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteBlobsForVehicle_RemovesEveryFileOfTheVehicleOnly_AndSwallowsStorageFailures()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-a");
        var other = await InsertVehicleAsync(harness, "owner-a");
        var first = await SeedDocumentAsync(harness, vehicle.Id);
        var second = await SeedDocumentAsync(harness, vehicle.Id);
        var kept = await SeedDocumentAsync(harness, other.Id);

        harness.Store.Fail = true;
        await harness.Documents.DeleteBlobsForVehicleAsync(vehicle.Id);
        harness.Store.Fail = false;
        Assert.True(await BlobExistsAsync(harness, vehicle.Id, first));

        await harness.Documents.DeleteBlobsForVehicleAsync(vehicle.Id);

        Assert.False(await BlobExistsAsync(harness, vehicle.Id, first));
        Assert.False(await BlobExistsAsync(harness, vehicle.Id, second));
        Assert.True(await BlobExistsAsync(harness, other.Id, kept));
    }
}

[Collection(DatabaseCollection.Name)]
public class AzureBlobVehicleDocumentStoreTests(AzuriteFixture azurite)
{
    private static async Task<(AzureBlobVehicleDocumentStore Store, BlobContainerClient Documents, BlobContainerClient Intake)> CreateAsync(AzuriteFixture azurite)
    {
        var documents = $"docs-{Guid.NewGuid():N}";
        var intake = $"intake-{Guid.NewGuid():N}";
        var store = new AzureBlobVehicleDocumentStore(azurite.Service, documents, intake);
        await store.EnsureContainerAsync(CancellationToken.None);
        await new AzureBlobIntakeStore(azurite.Service, intake).EnsureContainerAsync(CancellationToken.None);
        return (store, azurite.Service.GetBlobContainerClient(documents), azurite.Service.GetBlobContainerClient(intake));
    }

    [Fact]
    public async Task EnsureContainer_CreatesAPrivateContainer()
    {
        var (_, documents, _) = await CreateAsync(azurite);

        var properties = (await documents.GetPropertiesAsync()).Value;

        Assert.Equal(PublicAccessType.None, properties.PublicAccess ?? PublicAccessType.None);
    }

    [Fact]
    public async Task CopyFromIntake_CopiesTheIntakeBlobWithItsContentType_AndLeavesTheSourceInPlace()
    {
        var (store, documents, intake) = await CreateAsync(azurite);
        var submissionId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await EmailIntakeTestData.UploadBlobAsync(azurite.Service, intake.Name, $"{submissionId}/{attachmentId}", EmailIntakeTestData.PdfBytes, "application/pdf");

        var copied = await store.CopyFromIntakeAsync(submissionId, attachmentId, vehicleId, documentId, CancellationToken.None);

        Assert.True(copied);
        var target = documents.GetBlobClient($"{vehicleId}/{documentId}");
        Assert.True(await target.ExistsAsync());
        Assert.Equal("application/pdf", (await target.GetPropertiesAsync()).Value.ContentType);
        await using var stream = await store.OpenReadAsync(vehicleId, documentId, CancellationToken.None);
        using var buffer = new MemoryStream();
        await stream!.CopyToAsync(buffer);
        Assert.Equal(EmailIntakeTestData.PdfBytes, buffer.ToArray());
        Assert.True(await intake.GetBlobClient($"{submissionId}/{attachmentId}").ExistsAsync());
    }

    [Fact]
    public async Task CopyFromIntake_WhenTheSourceIsMissing_ReturnsFalseAndCreatesNothing()
    {
        var (store, documents, _) = await CreateAsync(azurite);
        var vehicleId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        Assert.False(await store.CopyFromIntakeAsync(Guid.NewGuid(), Guid.NewGuid(), vehicleId, documentId, CancellationToken.None));
        Assert.False(await documents.GetBlobClient($"{vehicleId}/{documentId}").ExistsAsync());
    }

    [Fact]
    public async Task OpenRead_ReturnsNullWhenMissing_AndDeleteIsIdempotent()
    {
        var (store, _, _) = await CreateAsync(azurite);

        Assert.Null(await store.OpenReadAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
        await store.DeleteAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
    }

    [Fact]
    public async Task DeleteVehicle_RemovesEveryBlobUnderTheVehiclePrefixOnly()
    {
        var (store, documents, _) = await CreateAsync(azurite);
        var vehicle = Guid.NewGuid();
        var other = Guid.NewGuid();
        await EmailIntakeTestData.UploadBlobAsync(azurite.Service, documents.Name, $"{vehicle}/a", [1], "application/pdf");
        await EmailIntakeTestData.UploadBlobAsync(azurite.Service, documents.Name, $"{vehicle}/b", [2], "application/pdf");
        await EmailIntakeTestData.UploadBlobAsync(azurite.Service, documents.Name, $"{other}/c", [3], "application/pdf");

        await store.DeleteVehicleAsync(vehicle, CancellationToken.None);

        Assert.False(await documents.GetBlobClient($"{vehicle}/a").ExistsAsync());
        Assert.False(await documents.GetBlobClient($"{vehicle}/b").ExistsAsync());
        Assert.True(await documents.GetBlobClient($"{other}/c").ExistsAsync());
    }

    [Fact]
    public async Task StorageFailure_RaisesDocumentStorageUnavailableException()
    {
        // Nothing listens on this endpoint, so every call fails to connect.
        var service = new BlobServiceClient(
            "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:1/devstoreaccount1;",
            new BlobClientOptions { Retry = { MaxRetries = 0, NetworkTimeout = TimeSpan.FromSeconds(5) } });
        var store = new AzureBlobVehicleDocumentStore(service, "docs-down", "intake-down");

        await Assert.ThrowsAsync<DocumentStorageUnavailableException>(() => store.EnsureContainerAsync(CancellationToken.None));
        await Assert.ThrowsAsync<DocumentStorageUnavailableException>(() => store.OpenReadAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<DocumentStorageUnavailableException>(() => store.CopyFromIntakeAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }
}
