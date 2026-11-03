using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Tests.Common;
using MyGarage.Database.VehicleDocuments;
using MyGarage.Shared.Vehicles;
using MyGarage.Shared.VehicleDocuments;
using static MyGarage.Api.Tests.ServiceRecords.ServiceRecordTestData;

namespace MyGarage.Api.Tests.VehicleDocuments;

public class VehicleDocumentsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-doc-oid";
    private const string Bob = "bob-doc-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient AliceClient() => factory.CreateClientForUser(Alice, "alice@contoso.test");

    [Fact]
    public async Task List_PagesTheVehiclesDocumentsNewestFirst()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice);
        var other = await factory.InsertVehicleAsync(Alice);
        var now = DateTimeOffset.UtcNow;
        var oldest = await factory.InsertVehicleDocumentAsync(vehicle, createdAt: now.AddDays(-2));
        var middle = await factory.InsertVehicleDocumentAsync(vehicle, IntakeClient.PngBytes, "scan.png", "image/png", createdAt: now.AddDays(-1));
        var newest = await factory.InsertVehicleDocumentAsync(vehicle, createdAt: now);
        await factory.InsertVehicleDocumentAsync(other);

        var first = (await (await AliceClient().GetAsync($"/api/vehicles/{vehicle}/documents?pageSize=2")).Content.ReadFromJsonAsync<PagedResultDto<VehicleDocumentDto>>())!;
        var second = (await (await AliceClient().GetAsync($"/api/vehicles/{vehicle}/documents?pageSize=2&page=2")).Content.ReadFromJsonAsync<PagedResultDto<VehicleDocumentDto>>())!;
        var beyond = (await (await AliceClient().GetAsync($"/api/vehicles/{vehicle}/documents?pageSize=2&page=9")).Content.ReadFromJsonAsync<PagedResultDto<VehicleDocumentDto>>())!;

        Assert.Equal([newest, middle], first.Items.Select(item => item.Id));
        Assert.Equal([oldest], second.Items.Select(item => item.Id));
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(2, beyond.Page);
        Assert.Equal("scan.png", first.Items[1].FileName);
        Assert.Equal("image/png", first.Items[1].ContentType);
        Assert.Equal(IntakeClient.PngBytes.Length, first.Items[1].SizeBytes);
        Assert.Equal(MyGarage.Shared.Drafts.DraftKind.ServiceRecord, first.Items[0].SourceKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await AliceClient().GetAsync($"/api/vehicles/{vehicle}/documents?page=0")).StatusCode);
    }

    [Fact]
    public async Task List_ForAnotherUsersOrUnknownVehicle_Is404()
    {
        var theirs = await factory.InsertVehicleAsync(Bob);
        await factory.InsertVehicleDocumentAsync(theirs);

        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().GetAsync($"/api/vehicles/{theirs}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().GetAsync($"/api/vehicles/{Guid.NewGuid()}/documents")).StatusCode);
    }

    [Fact]
    public async Task Content_StreamsTheFileWithItsTypeAndNosniff()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice);
        var id = await factory.InsertVehicleDocumentAsync(vehicle);

        var response = await AliceClient().GetAsync($"/api/vehicles/{vehicle}/documents/{id}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(IntakeClient.PdfBytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Content_AndDelete_Are404ForAnotherUsersDocument_IncludingTheDocumentIdThroughTheCallersOwnVehicle()
    {
        var mine = await factory.InsertVehicleAsync(Alice);
        var theirs = await factory.InsertVehicleAsync(Bob);
        var theirDocument = await factory.InsertVehicleDocumentAsync(theirs);

        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().GetAsync($"/api/vehicles/{theirs}/documents/{theirDocument}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().GetAsync($"/api/vehicles/{mine}/documents/{theirDocument}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().DeleteAsync($"/api/vehicles/{theirs}/documents/{theirDocument}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().DeleteAsync($"/api/vehicles/{mine}/documents/{theirDocument}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().GetAsync($"/api/vehicles/{mine}/documents/{Guid.NewGuid()}/content")).StatusCode);
        Assert.Equal(1L, await factory.CountRowsAsync("vehicle_documents"));
        Assert.True(await factory.DocumentBlobExistsAsync(theirs, theirDocument));
    }

    [Fact]
    public async Task Delete_Returns204_AndRemovesTheRowAndTheFile()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice);
        var id = await factory.InsertVehicleDocumentAsync(vehicle);

        var response = await AliceClient().DeleteAsync($"/api/vehicles/{vehicle}/documents/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0L, await factory.CountRowsAsync("vehicle_documents"));
        Assert.False(await factory.DocumentBlobExistsAsync(vehicle, id));
        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().DeleteAsync($"/api/vehicles/{vehicle}/documents/{id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_DoesNotChangeTheRecordTheDocumentWasKeptWith()
    {
        var client = AliceClient();
        var vehicle = await CreateVehicleAsync(client);
        var record = await CreateRecordAsync(client, vehicle.Id);
        var id = await factory.InsertVehicleDocumentAsync(vehicle.Id);
        await factory.ExecuteScalarAsync("UPDATE vehicle_documents SET source_record_id = @r WHERE id = @id", ("r", record.Id), ("id", id));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/vehicles/{vehicle.Id}/documents/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/vehicles/{vehicle.Id}/service-records/{record.Id}")).StatusCode);
    }

    [Fact]
    public async Task WhenBlobStorageIsDown_ContentAndDeleteAre503_AndTheRowStays()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice);
        var id = await factory.InsertVehicleDocumentAsync(vehicle);
        using var broken = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IVehicleDocumentBlobStore>();
            services.AddSingleton<IVehicleDocumentBlobStore>(new DownStore());
        }));
        var client = broken.CreateUserClient(Alice, "alice@contoso.test");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync($"/api/vehicles/{vehicle}/documents/{id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.DeleteAsync($"/api/vehicles/{vehicle}/documents/{id}")).StatusCode);
        Assert.Equal(1L, await factory.CountRowsAsync("vehicle_documents"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/vehicles/{vehicle}/documents")).StatusCode);
    }

    private sealed class DownStore : IVehicleDocumentBlobStore
    {
        private static DocumentStorageUnavailableException Down() => new("down");

        public Task EnsureContainerAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> CopyFromIntakeAsync(Guid submissionId, Guid attachmentId, Guid vehicleId, Guid documentId, CancellationToken cancellationToken) => throw Down();

        public Task<Stream?> OpenReadAsync(Guid vehicleId, Guid documentId, CancellationToken cancellationToken) => throw Down();

        public Task DeleteAsync(Guid vehicleId, Guid documentId, CancellationToken cancellationToken) => throw Down();

        public Task DeleteVehicleAsync(Guid vehicleId, CancellationToken cancellationToken) => throw Down();
    }

    [Fact]
    public async Task EveryRoute_Is401WithoutAToken_AndRefusesTheWorkflowToken()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice);
        var id = await factory.InsertVehicleDocumentAsync(vehicle);
        (HttpMethod Method, string Path)[] routes =
        [
            (HttpMethod.Get, $"/api/vehicles/{vehicle}/documents"),
            (HttpMethod.Get, $"/api/vehicles/{vehicle}/documents/{id}/content"),
            (HttpMethod.Delete, $"/api/vehicles/{vehicle}/documents/{id}"),
        ];
        var anonymous = factory.CreateClientWithToken(null);
        var workflow = factory.CreateClientForWorkflow();

        foreach (var (method, path) in routes)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(new HttpRequestMessage(method, path))).StatusCode);
            var status = (await workflow.SendAsync(new HttpRequestMessage(method, path))).StatusCode;
            Assert.True(status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized, $"{method} {path} answered {(int)status}");
        }

        Assert.Equal(1L, await factory.CountRowsAsync("vehicle_documents"));
    }

    // ---- VehicleService delete ----

    [Fact]
    public async Task DeletingAVehicle_RemovesItsDocumentsAndFiles_AndKeepsItsDraftsDetached()
    {
        var client = AliceClient();
        var vehicle = await CreateVehicleAsync(client);
        var other = await CreateVehicleAsync(client);
        var first = await factory.InsertVehicleDocumentAsync(vehicle.Id);
        var second = await factory.InsertVehicleDocumentAsync(vehicle.Id);
        var kept = await factory.InsertVehicleDocumentAsync(other.Id);
        var submission = await factory.InsertSubmissionAsync(Alice, status: "ready");
        var draft = await factory.InsertDraftAsync(Alice, submission, vehicleId: vehicle.Id);

        var response = await client.DeleteAsync($"/api/vehicles/{vehicle.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1L, await factory.CountRowsAsync("vehicle_documents"));
        Assert.False(await factory.DocumentBlobExistsAsync(vehicle.Id, first));
        Assert.False(await factory.DocumentBlobExistsAsync(vehicle.Id, second));
        Assert.True(await factory.DocumentBlobExistsAsync(other.Id, kept));
        Assert.Equal("needs_review", await factory.ExecuteScalarAsync("SELECT state FROM maintenance_drafts WHERE id = @id", ("id", draft)));
        Assert.Null(await factory.ExecuteScalarAsync("SELECT vehicle_id FROM maintenance_drafts WHERE id = @id", ("id", draft)));
    }

    [Fact]
    public async Task DeletingAVehicle_WhenDocumentStorageIsDown_StillDeletesTheVehicle()
    {
        var vehicle = await factory.InsertVehicleAsync(Alice);
        await factory.InsertVehicleDocumentAsync(vehicle);
        using var broken = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IVehicleDocumentBlobStore>();
            services.AddSingleton<IVehicleDocumentBlobStore>(new DownStore());
        }));

        var response = await broken.CreateUserClient(Alice, "alice@contoso.test").DeleteAsync($"/api/vehicles/{vehicle}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0L, await factory.CountRowsAsync("vehicle_documents"));
    }
}
