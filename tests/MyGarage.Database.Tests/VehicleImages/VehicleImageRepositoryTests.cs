using Azure.Storage.Blobs;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MyGarage.Database.Common;
using MyGarage.Database.Tests.Common;
using MyGarage.Database.VehicleImages;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.VehicleImages;

[Collection(DatabaseCollection.Name)]
public class VehicleImageRepositoryTests(PostgresFixture postgres, AzuriteFixture azurite)
{
    private sealed class Harness
    {
        public required NpgsqlDataSource DataSource { get; init; }

        public required VehicleRepository Vehicles { get; init; }

        public required VehicleImageRepository Images { get; init; }

        public required RecordingBlobStore Blobs { get; init; }

        public required BlobContainerClient Container { get; init; }
    }

    private async Task<Harness> CreateAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var containerName = $"img-{Guid.NewGuid():N}";
        var store = new AzureBlobVehicleImageStore(azurite.Service, containerName);
        await store.EnsureContainerAsync(CancellationToken.None);
        var blobs = new RecordingBlobStore(store);
        var scripts = new SqlScriptStore();
        return new Harness
        {
            DataSource = dataSource,
            Vehicles = new VehicleRepository(dataSource, scripts),
            Images = new VehicleImageRepository(dataSource, scripts, blobs, NullLogger<VehicleImageRepository>.Instance),
            Blobs = blobs,
            Container = azurite.Service.GetBlobContainerClient(containerName),
        };
    }

    private static async Task<VehicleRecord> InsertVehicleAsync(Harness harness, string ownerId)
    {
        var vehicle = TestData.Vehicle(ownerId);
        await harness.Vehicles.InsertAsync(vehicle, CancellationToken.None);
        return vehicle;
    }

    private static Task<AddImageResult> AddAsync(
        Harness harness, string ownerId, Guid vehicleId, byte[]? bytes = null, string contentType = "image/png", DateTimeOffset? at = null, string? caption = null)
    {
        var data = bytes ?? [1, 2, 3, 4];
        return harness.Images.AddAsync(
            ownerId, vehicleId, Guid.CreateVersion7(), contentType, data.Length, caption, new MemoryStream(data), at ?? TestData.Created, CancellationToken.None);
    }

    private static async Task<List<string>> BlobNamesAsync(Harness harness)
    {
        var names = new List<string>();
        await foreach (var blob in harness.Container.GetBlobsAsync())
        {
            names.Add(blob.Name);
        }

        return names;
    }

    [Fact]
    public async Task Add_FirstImage_BecomesMainAndTheBlobIsStoredWithItsContentType()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");

        var result = await AddAsync(harness, "owner-1", vehicle.Id, [9, 8, 7], "image/webp", caption: "Front");

        Assert.Equal(AddImageStatus.Added, result.Status);
        Assert.NotNull(result.Image);
        Assert.True(result.Image.IsMain);
        Assert.Equal("Front", result.Image.Caption);
        Assert.Equal(3, result.Image.SizeBytes);
        var blob = harness.Container.GetBlobClient($"{vehicle.Id}/{result.Image.Id}");
        Assert.True(await blob.ExistsAsync());
        var properties = (await blob.GetPropertiesAsync()).Value;
        Assert.Equal("image/webp", properties.ContentType);
        Assert.Equal(3, properties.ContentLength);
    }

    [Fact]
    public async Task Add_SecondImage_IsNotMain()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");
        await AddAsync(harness, "owner-1", vehicle.Id);

        var second = await AddAsync(harness, "owner-1", vehicle.Id);

        Assert.False(second.Image!.IsMain);
        Assert.Equal(1, (await harness.Images.ListAsync("owner-1", vehicle.Id, CancellationToken.None)).Count(image => image.IsMain));
    }

    [Fact]
    public async Task Add_The11thImage_ReachesTheLimitAndWritesNothing()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(AddImageStatus.Added, (await AddAsync(harness, "owner-1", vehicle.Id)).Status);
        }

        var uploadsBefore = harness.Blobs.Uploads;
        var result = await AddAsync(harness, "owner-1", vehicle.Id);

        Assert.Equal(AddImageStatus.LimitReached, result.Status);
        Assert.Equal(uploadsBefore, harness.Blobs.Uploads);
        Assert.Equal(10, (await BlobNamesAsync(harness)).Count);
        Assert.Equal(10, (await harness.Images.ListAsync("owner-1", vehicle.Id, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Add_AnotherOwnersVehicle_IsNotFoundWithNoRowAndNoBlob()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");

        var result = await AddAsync(harness, "owner-2", vehicle.Id);

        Assert.Equal(AddImageStatus.VehicleNotFound, result.Status);
        Assert.Equal(0, harness.Blobs.Uploads);
        Assert.Empty(await BlobNamesAsync(harness));
        await using var connection = await harness.DataSource.OpenConnectionAsync();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM vehicle_images"));
    }

    [Fact]
    public async Task Add_WhenTheUploadFails_RollsBackTheRow()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");
        harness.Blobs.FailUploads = true;

        await Assert.ThrowsAsync<ImageStorageUnavailableException>(() => AddAsync(harness, "owner-1", vehicle.Id));

        Assert.Empty(await harness.Images.ListAsync("owner-1", vehicle.Id, CancellationToken.None));
        Assert.Empty(await BlobNamesAsync(harness));
    }

    [Fact]
    public async Task SetMain_MovesTheMainFlagWithoutEverHavingTwo()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");
        var first = (await AddAsync(harness, "owner-1", vehicle.Id, at: TestData.Created)).Image!;
        var second = (await AddAsync(harness, "owner-1", vehicle.Id, at: TestData.Created.AddMinutes(1))).Image!;

        var changed = await harness.Images.SetMainAsync("owner-1", vehicle.Id, second.Id, CancellationToken.None);

        Assert.True(changed);
        var images = await harness.Images.ListAsync("owner-1", vehicle.Id, CancellationToken.None);
        Assert.Equal([second.Id], images.Where(image => image.IsMain).Select(image => image.Id));
        Assert.False(images.Single(image => image.Id == first.Id).IsMain);
    }

    [Fact]
    public async Task SetMain_UnknownImageAnotherOwnerOrWrongVehicle_ReturnsFalseAndChangesNothing()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");
        var other = await InsertVehicleAsync(harness, "owner-1");
        var main = (await AddAsync(harness, "owner-1", vehicle.Id)).Image!;
        var second = (await AddAsync(harness, "owner-1", vehicle.Id)).Image!;

        Assert.False(await harness.Images.SetMainAsync("owner-1", vehicle.Id, Guid.NewGuid(), CancellationToken.None));
        Assert.False(await harness.Images.SetMainAsync("owner-2", vehicle.Id, second.Id, CancellationToken.None));
        Assert.False(await harness.Images.SetMainAsync("owner-1", other.Id, second.Id, CancellationToken.None));
        Assert.Equal(
            [main.Id],
            (await harness.Images.ListAsync("owner-1", vehicle.Id, CancellationToken.None)).Where(image => image.IsMain).Select(image => image.Id));
    }

    [Fact]
    public async Task Delete_RemovesTheRowThenTheBlob()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");
        var keep = (await AddAsync(harness, "owner-1", vehicle.Id)).Image!;
        var drop = (await AddAsync(harness, "owner-1", vehicle.Id)).Image!;

        var deleted = await harness.Images.DeleteAsync("owner-1", vehicle.Id, drop.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Equal([keep.Id], (await harness.Images.ListAsync("owner-1", vehicle.Id, CancellationToken.None)).Select(image => image.Id));
        Assert.Equal([$"{vehicle.Id}/{keep.Id}"], await BlobNamesAsync(harness));
    }

    [Fact]
    public async Task Delete_TheMainImage_PromotesTheOldestRemaining()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");
        var main = (await AddAsync(harness, "owner-1", vehicle.Id, at: TestData.Created)).Image!;
        var older = (await AddAsync(harness, "owner-1", vehicle.Id, at: TestData.Created.AddMinutes(1))).Image!;
        await AddAsync(harness, "owner-1", vehicle.Id, at: TestData.Created.AddMinutes(2));

        await harness.Images.DeleteAsync("owner-1", vehicle.Id, main.Id, CancellationToken.None);

        var images = await harness.Images.ListAsync("owner-1", vehicle.Id, CancellationToken.None);
        Assert.Equal([older.Id], images.Where(image => image.IsMain).Select(image => image.Id));
    }

    [Fact]
    public async Task Delete_TheLastImage_LeavesNoMainImage()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");
        var only = (await AddAsync(harness, "owner-1", vehicle.Id)).Image!;

        Assert.True(await harness.Images.DeleteAsync("owner-1", vehicle.Id, only.Id, CancellationToken.None));

        Assert.Empty(await harness.Images.ListAsync("owner-1", vehicle.Id, CancellationToken.None));
        Assert.Empty(await BlobNamesAsync(harness));
    }

    [Fact]
    public async Task Delete_WhenTheBlobDeleteFails_StillSucceedsAndTheRowIsGone()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");
        var image = (await AddAsync(harness, "owner-1", vehicle.Id)).Image!;
        harness.Blobs.FailDeletes = true;

        var deleted = await harness.Images.DeleteAsync("owner-1", vehicle.Id, image.Id, CancellationToken.None);

        Assert.True(deleted);
        Assert.Empty(await harness.Images.ListAsync("owner-1", vehicle.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_AnotherOwnersOrUnknownImage_ReturnsFalseAndTouchesNoBlob()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");
        var image = (await AddAsync(harness, "owner-1", vehicle.Id)).Image!;

        Assert.False(await harness.Images.DeleteAsync("owner-2", vehicle.Id, image.Id, CancellationToken.None));
        Assert.False(await harness.Images.DeleteAsync("owner-1", vehicle.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(0, harness.Blobs.Deletes);
        Assert.Single(await BlobNamesAsync(harness));
        Assert.Single(await harness.Images.ListAsync("owner-1", vehicle.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteBlobsForVehicle_RemovesEveryBlobOfThatVehicleOnly()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");
        var other = await InsertVehicleAsync(harness, "owner-1");
        await AddAsync(harness, "owner-1", vehicle.Id);
        await AddAsync(harness, "owner-1", vehicle.Id);
        var kept = (await AddAsync(harness, "owner-1", other.Id)).Image!;

        await harness.Vehicles.DeleteAsync("owner-1", vehicle.Id, CancellationToken.None);
        await harness.Images.DeleteBlobsForVehicleAsync(vehicle.Id);

        Assert.Equal([$"{other.Id}/{kept.Id}"], await BlobNamesAsync(harness));
    }

    [Fact]
    public async Task DeleteBlobsForVehicle_WhenStorageFails_DoesNotThrow()
    {
        var harness = await CreateAsync();
        harness.Blobs.FailDeletes = true;

        await harness.Images.DeleteBlobsForVehicleAsync(Guid.NewGuid());

        Assert.Equal(1, harness.Blobs.Deletes);
    }

    [Fact]
    public async Task OpenContent_ReturnsTheStoredBytesAndContentType()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");
        var image = (await AddAsync(harness, "owner-1", vehicle.Id, [10, 20, 30], "image/jpeg")).Image!;

        await using var content = await harness.Images.OpenContentAsync("owner-1", vehicle.Id, image.Id, CancellationToken.None);

        Assert.NotNull(content);
        Assert.Equal("image/jpeg", content.ContentType);
        using var buffer = new MemoryStream();
        await content.Content.CopyToAsync(buffer);
        Assert.Equal([10, 20, 30], buffer.ToArray());
    }

    [Fact]
    public async Task OpenContent_AnotherOwnerOrWrongVehicle_IsNotFoundAndNeverReadsTheBlob()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");
        var other = await InsertVehicleAsync(harness, "owner-1");
        var image = (await AddAsync(harness, "owner-1", vehicle.Id)).Image!;

        Assert.Null(await harness.Images.OpenContentAsync("owner-2", vehicle.Id, image.Id, CancellationToken.None));
        Assert.Null(await harness.Images.OpenContentAsync("owner-1", other.Id, image.Id, CancellationToken.None));
        Assert.Null(await harness.Images.OpenContentAsync("owner-1", vehicle.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(0, harness.Blobs.Reads);
    }

    [Fact]
    public async Task OpenContent_WhenTheBlobIsMissing_IsNotFound()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");
        var image = (await AddAsync(harness, "owner-1", vehicle.Id)).Image!;
        await harness.Container.DeleteBlobAsync($"{vehicle.Id}/{image.Id}");

        Assert.Null(await harness.Images.OpenContentAsync("owner-1", vehicle.Id, image.Id, CancellationToken.None));
    }

    [Fact]
    public async Task List_NeverReadsBlobContent_AndIsScopedToTheOwner()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");
        await AddAsync(harness, "owner-1", vehicle.Id);

        Assert.Single(await harness.Images.ListAsync("owner-1", vehicle.Id, CancellationToken.None));
        Assert.Empty(await harness.Images.ListAsync("owner-2", vehicle.Id, CancellationToken.None));
        Assert.Equal(0, harness.Blobs.Reads);
    }

    [Fact]
    public async Task VehicleSummary_ExposesTheMainImageId()
    {
        var harness = await CreateAsync();
        var vehicle = await InsertVehicleAsync(harness, "owner-1");
        var main = (await AddAsync(harness, "owner-1", vehicle.Id)).Image!;
        await AddAsync(harness, "owner-1", vehicle.Id);

        var page = await harness.Vehicles.ListPageAsync("owner-1", VehicleSearchCriteria.None, 1, 12, CancellationToken.None);

        Assert.Equal(main.Id, page.Items.Single().MainImageId);
    }
}
