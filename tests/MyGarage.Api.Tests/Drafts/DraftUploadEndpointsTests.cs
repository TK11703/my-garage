using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Api.Tests.Common;
using MyGarage.Database.EmailIntake;
using MyGarage.Shared.Drafts;

namespace MyGarage.Api.Tests.Drafts;

public class DraftUploadEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";
    private const string Url = "/api/drafts/uploads";

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    public async Task InitializeAsync()
    {
        await factory.ResetDataAsync();
        factory.Extractor.Reset();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient AliceClient() => factory.CreateClientForUser(Alice, "alice@contoso.test");

    private static ExtractionResult ServiceRecord() =>
        new()
        {
            Kind = ExtractionKind.ServiceRecord,
            Confidence = 0.87m,
            Notes = "Oil change invoice",
            Date = "2026-10-12",
            OdometerReading = 48210,
            ShopOrPerson = "Main St Garage",
            LineItems = [new ExtractedLineItem("Oil change", 59.99m, null, null, null)],
        };

    private static MultipartFormDataContent Form(IEnumerable<(string Name, byte[] Bytes)> files, string? vehicleId = null)
    {
        var content = new MultipartFormDataContent { { new StringContent("x"), "note" } };
        foreach (var (name, bytes) in files)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(file, "files", name);
        }

        if (vehicleId is not null)
        {
            content.Add(new StringContent(vehicleId), "vehicleId");
        }

        return content;
    }

    private static MultipartFormDataContent Pdf(string name = "invoice.pdf") => Form([(name, IntakeClient.PdfBytes)]);

    private async Task<DraftUploadResultDto> UploadAsync(HttpClient client, MultipartFormDataContent form)
    {
        var response = await client.PostAsync(Url, form);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DraftUploadResultDto>())!;
    }

    private async Task<string?> ScalarAsync(string sql, params (string Name, object? Value)[] parameters) =>
        (await factory.ExecuteScalarAsync(sql, parameters))?.ToString();

    private async Task<Guid> OnlyDraftIdAsync() => (Guid)(await factory.ExecuteScalarAsync("SELECT id FROM maintenance_drafts"))!;

    private async Task<List<string>> BlobNamesAsync()
    {
        var names = new List<string>();
        await foreach (var blob in factory.IntakeContainer.GetBlobsAsync())
        {
            names.Add(blob.Name);
        }

        return names;
    }

    private async Task ProcessAsync() => Assert.True(await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None));

    private async Task<Dictionary<string, string[]>> ErrorsAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.ToDictionary(pair => pair.Key.ToLowerInvariant(), pair => pair.Value);

    // ---- queueing ----

    [Fact]
    public async Task APdf_Is202_AndIsQueuedAsAnUploadSubmissionOwnedByTheCaller_WithNothingCreatedYet()
    {
        var result = await UploadAsync(AliceClient(), Pdf());

        Assert.Equal(1, result.AcceptedCount);
        Assert.Empty(result.Refused);
        Assert.Equal("upload|alice-oid|queued", await ScalarAsync("SELECT origin || '|' || owner_id || '|' || status FROM email_submissions"));
        Assert.Equal(result.SubmissionId.ToString(), await ScalarAsync("SELECT id FROM email_submissions"));
        Assert.Equal("True", await ScalarAsync("SELECT message_id LIKE 'upload:%' FROM email_submissions"));
        Assert.Equal(1L, await factory.CountRowsAsync("email_submissions"));
        Assert.Equal(1L, (long)(await factory.ExecuteScalarAsync("SELECT count(*) FROM email_submissions WHERE sender IS NULL AND subject IS NULL AND body_text IS NULL"))!);
        Assert.Equal("alice-oid|invoice.pdf|application/pdf", await ScalarAsync("SELECT owner_id || '|' || file_name || '|' || content_type FROM email_attachments"));
        var attachmentId = (Guid)(await factory.ExecuteScalarAsync("SELECT id FROM email_attachments"))!;
        Assert.True(await factory.IntakeBlobExistsAsync(result.SubmissionId, attachmentId));

        foreach (var table in new[] { "maintenance_drafts", "notifications", "vehicles", "service_records", "fuel_energy_entries", "inspections", "registrations", "insurance_policies", "taxes" })
        {
            Assert.Equal(0L, await factory.CountRowsAsync(table));
        }
    }

    [Theory]
    [InlineData("receipt.png", "image/png")]
    [InlineData("photo.jpg", "image/jpeg")]
    public async Task AnImage_IsAcceptedAndTypedFromItsBytes(string name, string contentType)
    {
        var bytes = contentType == "image/png" ? IntakeClient.PngBytes : Jpeg;

        var result = await UploadAsync(AliceClient(), Form([(name, bytes)]));

        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(contentType, await ScalarAsync("SELECT content_type FROM email_attachments"));
    }

    [Fact]
    public async Task TheClaimedTypeAndFileNameAreNotTrusted_ButTheBytesAre()
    {
        var response = await AliceClient().PostAsync(Url, Form([("invoice.pdf", "just some text"u8.ToArray())]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ErrorsAsync(response);
        Assert.Contains("files[0]", errors.Keys);
        Assert.Equal(0L, await factory.CountRowsAsync("email_submissions"));
        Assert.Equal(0L, await factory.CountRowsAsync("email_attachments"));
        Assert.Empty(await BlobNamesAsync());
    }

    [Fact]
    public async Task AnEmptyFile_AndAFileOverTheLimit_AreEachRefusedWithTheirOwnMessage_AndNothingIsStored()
    {
        var over = new byte[DraftUploadLimits.MaxFileBytes + 1];
        IntakeClient.PdfBytes.CopyTo(over, 0);

        var empty = await AliceClient().PostAsync(Url, Form([("empty.pdf", [])]));
        var large = await AliceClient().PostAsync(Url, Form([("big.pdf", over)]));

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Contains("empty", (await ErrorsAsync(empty))["files[0]"][0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, large.StatusCode);
        Assert.Contains("10 MB", (await ErrorsAsync(large))["files[0]"][0], StringComparison.Ordinal);
        Assert.Equal(0L, await factory.CountRowsAsync("email_submissions"));
        Assert.Empty(await BlobNamesAsync());
    }

    [Fact]
    public async Task ARequestOverTheBodyLimit_Is413_AndNothingIsStored()
    {
        var big = new byte[17 * 1024 * 1024];
        IntakeClient.PdfBytes.CopyTo(big, 0);

        var response = await AliceClient().PostAsync(Url, Form([("big.pdf", big)]));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0L, await factory.CountRowsAsync("email_submissions"));
        Assert.Empty(await BlobNamesAsync());
    }

    [Fact]
    public async Task NoFiles_Is400ValidationProblem_AndNothingIsStored()
    {
        var response = await AliceClient().PostAsync(Url, Form([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("files", (await ErrorsAsync(response)).Keys);
        Assert.Equal(0L, await factory.CountRowsAsync("email_submissions"));
    }

    // ---- reading, review, and the decision ----

    [Fact]
    public async Task TheProcessorTurnsTheUploadIntoANeedsReviewDraft_WithANotification_AndTheOriginalFileReadable()
    {
        await UploadAsync(AliceClient(), Pdf());
        factory.Extractor.Enqueue(ServiceRecord());

        await ProcessAsync();

        Assert.Equal("alice-oid|needs_review|service_record", await ScalarAsync("SELECT owner_id || '|' || state || '|' || kind FROM maintenance_drafts"));
        Assert.Equal(1L, await factory.CountRowsAsync("notifications"));
        Assert.Equal("ready", await ScalarAsync("SELECT status FROM email_submissions"));
        var draftId = await OnlyDraftIdAsync();
        var detail = (await AliceClient().GetFromJsonAsync<MaintenanceDraftDto>($"/api/drafts/{draftId}"))!;
        Assert.True(detail.HasAttachment);
        var original = await AliceClient().GetAsync($"/api/drafts/{draftId}/attachment");
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        Assert.Equal(IntakeClient.PdfBytes, await original.Content.ReadAsByteArrayAsync());
        Assert.Equal(0L, await factory.CountRowsAsync("service_records"));
        var call = Assert.Single(factory.Extractor.Calls);
        Assert.NotNull(call.Document);
    }

    [Fact]
    public async Task AcceptingTheUploadedDraftKeepingTheOriginal_CreatesTheRecordMarkedFromEmail_AndMovesTheFile()
    {
        var vehicleId = await factory.InsertVehicleAsync(Alice, "Ford", "F-150", 2018);
        var result = await UploadAsync(AliceClient(), Pdf());
        factory.Extractor.Enqueue(ServiceRecord());
        await ProcessAsync();
        var draftId = await OnlyDraftIdAsync();
        var attachmentId = (Guid)(await factory.ExecuteScalarAsync("SELECT id FROM email_attachments"))!;
        var detail = (await AliceClient().GetFromJsonAsync<MaintenanceDraftDto>($"/api/drafts/{draftId}"))!;

        var response = await AliceClient().PostAsJsonAsync(
            $"/api/drafts/{draftId}/accept", new AcceptDraftRequest { VehicleId = vehicleId, Proposal = detail.Proposal, KeepOriginal = true });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var accepted = (await response.Content.ReadFromJsonAsync<AcceptedDraftDto>())!;
        Assert.NotNull(accepted.DocumentId);
        Assert.Equal("True", await ScalarAsync("SELECT from_email FROM service_records"));
        Assert.True(await factory.DocumentBlobExistsAsync(vehicleId, accepted.DocumentId.Value));
        Assert.False(await factory.IntakeBlobExistsAsync(result.SubmissionId, attachmentId));
    }

    [Fact]
    public async Task DiscardingTheUploadedDraft_CreatesNoRecord_AndRemovesTheFile()
    {
        var result = await UploadAsync(AliceClient(), Pdf());
        factory.Extractor.Enqueue(ServiceRecord());
        await ProcessAsync();
        var draftId = await OnlyDraftIdAsync();
        var attachmentId = (Guid)(await factory.ExecuteScalarAsync("SELECT id FROM email_attachments"))!;

        var response = await AliceClient().PostAsync($"/api/drafts/{draftId}/discard", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0L, await factory.CountRowsAsync("service_records"));
        Assert.False(await factory.IntakeBlobExistsAsync(result.SubmissionId, attachmentId));
    }

    [Fact]
    public async Task AnotherUser_CannotSeeTheDraftOrItsFile()
    {
        await UploadAsync(AliceClient(), Pdf());
        factory.Extractor.Enqueue(ServiceRecord());
        await ProcessAsync();
        var draftId = await OnlyDraftIdAsync();
        var bob = factory.CreateClientForUser(Bob, "bob@contoso.test");

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/drafts/{draftId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/drafts/{draftId}/attachment")).StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<MyGarage.Shared.Vehicles.PagedResultDto<MaintenanceDraftSummaryDto>>("/api/drafts"))!.Items);
    }

    // ---- the vehicle chosen with Import Record ----

    [Fact]
    public async Task AVehicleOfTheCaller_IsStoredAsPreselected_AndTheDraftShowsItAsSelected()
    {
        var vehicleId = await factory.InsertVehicleAsync(Alice, "Ford", "F-150", 2018);
        await factory.InsertVehicleAsync(Alice, "Honda", "Civic", 2020);

        await UploadAsync(AliceClient(), Form([("invoice.pdf", IntakeClient.PdfBytes)], vehicleId.ToString()));

        Assert.Equal(vehicleId.ToString(), await ScalarAsync("SELECT preselected_vehicle_id FROM email_submissions"));
        factory.Extractor.Enqueue(ServiceRecord());
        await ProcessAsync();
        var draft = (await AliceClient().GetFromJsonAsync<MaintenanceDraftDto>($"/api/drafts/{await OnlyDraftIdAsync()}"))!;
        Assert.Equal(vehicleId, draft.Vehicle.VehicleId);
        Assert.Equal(VehicleMatch.Selected, draft.Vehicle.Match);
        Assert.Equal(2, draft.Vehicle.Candidates.Count);
    }

    [Fact]
    public async Task AnotherUsersVehicle_AndAnUnknownVehicle_Are404_AndNothingIsStored()
    {
        var bobsVehicle = await factory.InsertVehicleAsync(Bob, "Tesla", "Model 3", 2021);

        var other = await AliceClient().PostAsync(Url, Form([("invoice.pdf", IntakeClient.PdfBytes)], bobsVehicle.ToString()));
        var unknown = await AliceClient().PostAsync(Url, Form([("invoice.pdf", IntakeClient.PdfBytes)], Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(
            (await other.Content.ReadFromJsonAsync<ProblemDetails>())!.Title,
            (await unknown.Content.ReadFromJsonAsync<ProblemDetails>())!.Title);
        Assert.Equal(0L, await factory.CountRowsAsync("email_submissions"));
        Assert.Equal(0L, await factory.CountRowsAsync("email_attachments"));
        Assert.Empty(await BlobNamesAsync());
    }

    [Fact]
    public async Task AnEmptyVehicleId_Is400()
    {
        var response = await AliceClient().PostAsync(Url, Form([("invoice.pdf", IntakeClient.PdfBytes)], Guid.Empty.ToString()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("vehicleid", (await ErrorsAsync(response)).Keys);
        Assert.Equal(0L, await factory.CountRowsAsync("email_submissions"));
    }

    // ---- several files at once ----

    [Fact]
    public async Task ThreeFiles_AreOneSubmissionWithThreeAttachments_AndOnePassMakesThreeDraftsThatAreDecidedIndependently()
    {
        var vehicleId = await factory.InsertVehicleAsync(Alice, "Ford", "F-150", 2018);
        var result = await UploadAsync(AliceClient(), Form([("a.pdf", IntakeClient.PdfBytes), ("b.png", IntakeClient.PngBytes), ("c.pdf", IntakeClient.PdfBytes)]));
        Assert.Equal(3, result.AcceptedCount);
        Assert.Equal(1L, await factory.CountRowsAsync("email_submissions"));
        Assert.Equal(3L, await factory.CountRowsAsync("email_attachments"));
        for (var i = 0; i < 3; i++)
        {
            factory.Extractor.Enqueue(ServiceRecord());
        }

        await ProcessAsync();

        Assert.Equal(3L, await factory.CountRowsAsync("maintenance_drafts"));
        var page = (await AliceClient().GetFromJsonAsync<MyGarage.Shared.Vehicles.PagedResultDto<MaintenanceDraftSummaryDto>>("/api/drafts"))!;
        Assert.Equal(3, page.Items.Count);
        var detail = (await AliceClient().GetFromJsonAsync<MaintenanceDraftDto>($"/api/drafts/{page.Items[0].Id}"))!;
        var accepted = await AliceClient().PostAsJsonAsync(
            $"/api/drafts/{page.Items[0].Id}/accept", new AcceptDraftRequest { VehicleId = vehicleId, Proposal = detail.Proposal });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await AliceClient().PostAsync($"/api/drafts/{page.Items[1].Id}/discard", null)).StatusCode);
        var open = (await AliceClient().GetFromJsonAsync<MyGarage.Shared.Vehicles.PagedResultDto<MaintenanceDraftSummaryDto>>("/api/drafts"))!;
        Assert.Equal([page.Items[2].Id], open.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task ValidAndInvalidFilesTogether_QueueTheValidOnes_AndListTheInvalidByPosition()
    {
        var result = await UploadAsync(
            AliceClient(), Form([("a.pdf", IntakeClient.PdfBytes), ("notes.pdf", "plain text"u8.ToArray()), ("c.png", IntakeClient.PngBytes)]));

        Assert.Equal(2, result.AcceptedCount);
        var refused = Assert.Single(result.Refused);
        Assert.Equal(1, refused.Index);
        Assert.Equal(DraftUploadLimits.UnsupportedType, refused.Reason);
        Assert.Equal(2L, await factory.CountRowsAsync("email_attachments"));
        Assert.Equal(2, (await BlobNamesAsync()).Count);
    }

    [Fact]
    public async Task SixFiles_AndFilesOverTheTotalLimit_Are400_AndNothingIsStored()
    {
        var six = await AliceClient().PostAsync(Url, Form([.. Enumerable.Range(1, 6).Select(index => ($"{index}.pdf", IntakeClient.PdfBytes))]));
        var bytes = new byte[5_500_000];
        IntakeClient.PdfBytes.CopyTo(bytes, 0);
        var heavy = await AliceClient().PostAsync(Url, Form([("1.pdf", bytes), ("2.pdf", bytes), ("3.pdf", bytes)]));

        Assert.Equal(HttpStatusCode.BadRequest, six.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, heavy.StatusCode);
        Assert.Contains("files", (await ErrorsAsync(six)).Keys);
        Assert.Contains("files", (await ErrorsAsync(heavy)).Keys);
        Assert.Equal(0L, await factory.CountRowsAsync("email_submissions"));
        Assert.Empty(await BlobNamesAsync());
    }

    // ---- authorization and storage ----

    [Fact]
    public async Task WithoutAToken_Is401_AndAnAppOnlyWorkflowToken_Is403()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClientWithToken(null).PostAsync(Url, Pdf())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.CreateClientForWorkflow().PostAsync(Url, Pdf())).StatusCode);
        Assert.Equal(0L, await factory.CountRowsAsync("email_submissions"));
    }

    [Fact]
    public async Task AStorageOutage_Is503WithRetryAfter_AndNoSubmissionIsQueued()
    {
        using var broken = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IIntakeBlobStore>();
            services.AddSingleton<IIntakeBlobStore>(new DownIntakeStore());
        }));

        var response = await broken.CreateUserClient(Alice, "alice@contoso.test").PostAsync(Url, Pdf());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.Equal(0L, await factory.CountRowsAsync("email_submissions"));
    }

    private sealed class DownIntakeStore : IIntakeBlobStore
    {
        private static IntakeStorageUnavailableException Down() => new("The test made storage fail.", null);

        public Task EnsureContainerAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UploadAsync(Guid submissionId, Guid attachmentId, Stream content, string contentType, CancellationToken cancellationToken) => throw Down();

        public Task<Stream?> OpenReadAsync(Guid submissionId, Guid attachmentId, CancellationToken cancellationToken) => throw Down();

        public Task DeleteAsync(Guid submissionId, Guid attachmentId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListBlobNamesOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
