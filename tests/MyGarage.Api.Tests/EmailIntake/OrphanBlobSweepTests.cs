using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyGarage.Api.EmailIntake;
using MyGarage.Api.Tests.Common;
using MyGarage.Database.EmailIntake;

namespace MyGarage.Api.Tests.EmailIntake;

public class OrphanBlobSweepTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";

    public async Task InitializeAsync()
    {
        await factory.ResetDataAsync();
        await factory.IntakeContainer.CreateIfNotExistsAsync();
        await factory.DocumentContainer.CreateIfNotExistsAsync();
        await factory.ImageContainer.CreateIfNotExistsAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // Blobs are stamped by the storage service with the real time, so the test moves its clock instead.
    private OrphanBlobSweepService Sweep(TimeProvider clock) =>
        new(
            factory.Services.GetRequiredService<EmailSubmissionRepository>(),
            factory.Services.GetRequiredService<IIntakeBlobStore>(),
            clock,
            Options.Create(new SubmissionProcessingOptions()),
            NullLogger<OrphanBlobSweepService>.Instance);

    private static Task UploadAsync(BlobContainerClient container, string name) =>
        container.GetBlobClient(name).UploadAsync(new BinaryData(IntakeClient.PdfBytes), overwrite: true);

    private static async Task<List<string>> NamesAsync(BlobContainerClient container)
    {
        var names = new List<string>();
        await foreach (var blob in container.GetBlobsAsync())
        {
            names.Add(blob.Name);
        }

        return names.Order().ToList();
    }

    [Fact]
    public async Task ABlobWithoutARow_IsDeletedOnceItIsOlderThanOneHour()
    {
        var orphan = $"{Guid.NewGuid()}/{Guid.NewGuid()}";
        await UploadAsync(factory.IntakeContainer, orphan);

        var young = await Sweep(new TestTimeProvider(DateTimeOffset.UtcNow)).RunOnceAsync(CancellationToken.None);
        Assert.Equal(0, young);
        Assert.Equal([orphan], await NamesAsync(factory.IntakeContainer));

        var old = await Sweep(new TestTimeProvider(DateTimeOffset.UtcNow.AddHours(2))).RunOnceAsync(CancellationToken.None);
        Assert.Equal(1, old);
        Assert.Empty(await NamesAsync(factory.IntakeContainer));
    }

    [Fact]
    public async Task ABlobThatHasAnAttachmentRow_IsKeptEvenWhenOld()
    {
        var submission = await factory.InsertSubmissionAsync(Alice);
        var attachment = await factory.InsertAttachmentAsync(submission, Alice, content: IntakeClient.PdfBytes);
        var orphan = $"{submission}/{Guid.NewGuid()}";
        await UploadAsync(factory.IntakeContainer, orphan);

        var deleted = await Sweep(new TestTimeProvider(DateTimeOffset.UtcNow.AddHours(2))).RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Equal([$"{submission}/{attachment}"], await NamesAsync(factory.IntakeContainer));
    }

    [Fact]
    public async Task ABlobWhoseNameIsNotSubmissionSlashAttachment_IsLeftAlone()
    {
        await UploadAsync(factory.IntakeContainer, "readme.txt");
        await UploadAsync(factory.IntakeContainer, $"{Guid.NewGuid()}/not-a-guid");

        var deleted = await Sweep(new TestTimeProvider(DateTimeOffset.UtcNow.AddHours(2))).RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.Equal(2, (await NamesAsync(factory.IntakeContainer)).Count);
    }

    [Fact]
    public async Task TheSweep_NeverTouchesTheDocumentOrImageContainers()
    {
        var documentBlob = $"{Guid.NewGuid()}/{Guid.NewGuid()}";
        var imageBlob = $"{Guid.NewGuid()}/{Guid.NewGuid()}";
        await UploadAsync(factory.DocumentContainer, documentBlob);
        await UploadAsync(factory.ImageContainer, imageBlob);
        await UploadAsync(factory.IntakeContainer, $"{Guid.NewGuid()}/{Guid.NewGuid()}");

        await Sweep(new TestTimeProvider(DateTimeOffset.UtcNow.AddHours(2))).RunOnceAsync(CancellationToken.None);

        Assert.Equal([documentBlob], await NamesAsync(factory.DocumentContainer));
        Assert.Equal([imageBlob], await NamesAsync(factory.ImageContainer));
        Assert.Empty(await NamesAsync(factory.IntakeContainer));
    }

    [Fact]
    public async Task TheHostRegistersTheSweepAsAHostedService_ButItsLoopIsOffInTests()
    {
        var orphan = $"{Guid.NewGuid()}/{Guid.NewGuid()}";
        await UploadAsync(factory.IntakeContainer, orphan);

        Assert.Contains(factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), service => service is OrphanBlobSweepService);
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        Assert.Equal([orphan], await NamesAsync(factory.IntakeContainer));
    }
}
