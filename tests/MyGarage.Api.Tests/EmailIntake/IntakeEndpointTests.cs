using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Api.Tests.Common;
using MyGarage.Database.EmailIntake;
using MyGarage.Shared.EmailIntake;

namespace MyGarage.Api.Tests.EmailIntake;

public class IntakeEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string AliceEmail = "alice@contoso.test";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<List<string>> BlobNamesAsync()
    {
        var names = new List<string>();
        await foreach (var blob in factory.IntakeContainer.GetBlobsAsync())
        {
            names.Add(blob.Name);
        }

        return names;
    }

    private async Task<string?> ScalarAsync(string sql) => (await factory.ExecuteScalarAsync(sql))?.ToString();

    private async Task<(HttpResponseMessage Response, IntakeAcknowledgement Acknowledgement)> PostOkAsync(IntakeEmailRequest email)
    {
        var response = await IntakeClient.PostAsync(factory.CreateClientForWorkflow(), email);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (response, (await response.Content.ReadFromJsonAsync<IntakeAcknowledgement>())!);
    }

    [Fact]
    public async Task MatchedSender_Returns202_QueuesTheSubmission_AndStoresEveryAttachment()
    {
        await factory.InsertUserProfileAsync(Alice, AliceEmail);
        var email = IntakeClient.NewEmail(AliceEmail, subject: "Fwd: invoice", bodyText: "Oil change today").AddPdf("invoice.pdf").AddPng("receipt.png");

        var (_, acknowledgement) = await PostOkAsync(email);

        Assert.Equal("received", acknowledgement.Status);
        Assert.Equal(1L, await factory.CountRowsAsync("email_submissions"));
        Assert.Equal(
            $"queued|{Alice}|{AliceEmail}|Fwd: invoice|Oil change today",
            await ScalarAsync($"SELECT status || '|' || owner_id || '|' || sender || '|' || subject || '|' || body_text FROM email_submissions WHERE id = '{acknowledgement.SubmissionId}'"));
        var expectedPdf = $"invoice.pdf:application/pdf:{IntakeClient.PdfBytes.Length}:{Convert.ToHexStringLower(SHA256.HashData(IntakeClient.PdfBytes))}";
        var expectedPng = $"receipt.png:image/png:{IntakeClient.PngBytes.Length}:{Convert.ToHexStringLower(SHA256.HashData(IntakeClient.PngBytes))}";
        Assert.Equal(
            $"{expectedPdf},{expectedPng}",
            await ScalarAsync("SELECT string_agg(file_name || ':' || content_type || ':' || size_bytes || ':' || sha256, ',' ORDER BY file_name) FROM email_attachments"));
        Assert.Equal(Alice + "," + Alice, await ScalarAsync("SELECT string_agg(owner_id, ',') FROM email_attachments"));
        var expectedBlobs = (await ScalarAsync("SELECT string_agg(submission_id || '/' || id, ',' ORDER BY submission_id || '/' || id) FROM email_attachments"))!.Split(',');
        Assert.Equal(expectedBlobs, (await BlobNamesAsync()).Order());
        await using var stream = await factory.IntakeContainer.GetBlobClient(expectedBlobs[0]).OpenReadAsync();
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public async Task UnknownSender_Returns202WithTheSameShape_ButStoresOnlyTheMessageId()
    {
        await factory.InsertUserProfileAsync(Alice, AliceEmail);
        var matched = await PostOkAsync(IntakeClient.NewEmail(AliceEmail).AddPdf());
        var unknown = await PostOkAsync(IntakeClient.NewEmail("stranger@nowhere.test", subject: "secret subject", bodyText: "secret body").AddPdf("x.pdf").AddPng());

        static string[] Keys(string json) => [.. JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name)];
        Assert.Equal(Keys(await matched.Response.Content.ReadAsStringAsync()), Keys(await unknown.Response.Content.ReadAsStringAsync()));
        Assert.Equal(matched.Acknowledgement.Status, unknown.Acknowledgement.Status);
        Assert.Equal(
            "not_accepted|||||",
            await ScalarAsync(
                $"SELECT status || '|' || coalesce(owner_id, '') || '|' || coalesce(sender, '') || '|' || coalesce(subject, '') || '|' || coalesce(body_text, '') || '|' || coalesce(received_at::text, '') " +
                $"FROM email_submissions WHERE id = '{unknown.Acknowledgement.SubmissionId}'"));
        Assert.Equal("0", await ScalarAsync($"SELECT count(*) FROM email_attachments WHERE submission_id = '{unknown.Acknowledgement.SubmissionId}'"));
        Assert.Single(await BlobNamesAsync());
    }

    [Fact]
    public async Task SenderMatchingTwoProfiles_IsTreatedAsUnknown()
    {
        await factory.InsertUserProfileAsync("owner-1", AliceEmail);
        await factory.InsertUserProfileAsync("owner-2", AliceEmail.ToUpperInvariant());

        var (_, acknowledgement) = await PostOkAsync(IntakeClient.NewEmail(AliceEmail).AddPdf());

        Assert.Equal("not_accepted", await ScalarAsync($"SELECT status FROM email_submissions WHERE id = '{acknowledgement.SubmissionId}'"));
        Assert.Empty(await BlobNamesAsync());
    }

    [Fact]
    public async Task SenderComparison_IsCaseInsensitiveAndIgnoresSurroundingWhitespace()
    {
        await factory.InsertUserProfileAsync(Alice, "Alice@Contoso.Test");

        var (_, acknowledgement) = await PostOkAsync(IntakeClient.NewEmail("  ALICE@contoso.test \t"));

        Assert.Equal($"queued|{Alice}|ALICE@contoso.test", await ScalarAsync(
            $"SELECT status || '|' || owner_id || '|' || sender FROM email_submissions WHERE id = '{acknowledgement.SubmissionId}'"));
    }

    [Fact]
    public async Task InlineAttachments_AreNotStored()
    {
        await factory.InsertUserProfileAsync(Alice, AliceEmail);

        await PostOkAsync(IntakeClient.NewEmail(AliceEmail).AddPdf("invoice.pdf").AddPng("logo.png", isInline: true));

        Assert.Equal("invoice.pdf", await ScalarAsync("SELECT string_agg(file_name, ',') FROM email_attachments"));
        Assert.Single(await BlobNamesAsync());
    }

    [Fact]
    public async Task APdfNamedFileWhoseBytesAreText_IsSkippedAndTheRestIsStored()
    {
        await factory.InsertUserProfileAsync(Alice, AliceEmail);
        var email = IntakeClient.NewEmail(AliceEmail)
            .AddAttachment("fake.pdf", "application/pdf", "just some text, not a pdf"u8.ToArray())
            .AddPng("receipt.png");

        var (_, acknowledgement) = await PostOkAsync(email);

        Assert.Equal("receipt.png", await ScalarAsync("SELECT string_agg(file_name, ',') FROM email_attachments"));
        var skipped = await ScalarAsync($"SELECT skipped_attachments::text FROM email_submissions WHERE id = '{acknowledgement.SubmissionId}'");
        using var document = JsonDocument.Parse(skipped!);
        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("fake.pdf", item.GetProperty("fileName").GetString());
        Assert.Equal("unsupported_type", item.GetProperty("reason").GetString());
        Assert.Single(await BlobNamesAsync());
    }

    [Fact]
    public async Task TextOnlySubmission_IsQueuedWithoutAttachments()
    {
        await factory.InsertUserProfileAsync(Alice, AliceEmail);

        await PostOkAsync(IntakeClient.NewEmail(AliceEmail));

        Assert.Equal(1L, await factory.CountRowsAsync("email_submissions"));
        Assert.Equal(0L, await factory.CountRowsAsync("email_attachments"));
        Assert.Empty(await BlobNamesAsync());
    }

    [Fact]
    public async Task RepeatedMessageId_ReturnsTheSameSubmissionAndKeepsOnlyTheFirstAttachments()
    {
        await factory.InsertUserProfileAsync(Alice, AliceEmail);
        var email = IntakeClient.NewEmail(AliceEmail, messageId: "<dup@mail.test>").AddPdf();

        var first = await PostOkAsync(email);
        var second = await PostOkAsync(email);

        Assert.Equal(first.Acknowledgement.SubmissionId, second.Acknowledgement.SubmissionId);
        Assert.Equal(1L, await factory.CountRowsAsync("email_submissions"));
        Assert.Equal(1L, await factory.CountRowsAsync("email_attachments"));
        Assert.Single(await BlobNamesAsync());
    }

    private static ExtractionResult ServiceRecordResult() =>
        new()
        {
            Kind = ExtractionKind.ServiceRecord,
            Confidence = 0.9m,
            Date = "2026-10-12",
            OdometerReading = 48210,
            LineItems = [new ExtractedLineItem("Oil change", 59.99m, null, null, null)],
        };

    [Fact]
    public async Task RepeatedBody_ReturnsAnIdenticalAcknowledgement_AndStoresOneSubmissionWithOneSetOfAttachmentsAndBlobs()
    {
        await factory.InsertUserProfileAsync(Alice, AliceEmail);
        var email = IntakeClient.NewEmail(AliceEmail, messageId: "<same@mail.test>").AddPdf().AddPng();

        var first = await PostOkAsync(email);
        var second = await PostOkAsync(email);

        Assert.Equal(await first.Response.Content.ReadAsStringAsync(), await second.Response.Content.ReadAsStringAsync());
        Assert.Equal(1L, await factory.CountRowsAsync("email_submissions"));
        Assert.Equal(2L, await factory.CountRowsAsync("email_attachments"));
        Assert.Equal(2, (await BlobNamesAsync()).Count);
        Assert.Equal(
            await ScalarAsync("SELECT string_agg(submission_id || '/' || id, ',' ORDER BY id) FROM email_attachments"),
            string.Join(',', (await BlobNamesAsync()).OrderBy(name => name.Split('/')[1], StringComparer.Ordinal)));
    }

    [Fact]
    public async Task RepeatedUnmatchedBody_ReturnsTheSameAcknowledgement_AndStoresOneNotAcceptedRow()
    {
        var email = IntakeClient.NewEmail("stranger@nowhere.test", messageId: "<unknown@mail.test>").AddPdf();

        var first = await PostOkAsync(email);
        var second = await PostOkAsync(email);

        Assert.Equal(await first.Response.Content.ReadAsStringAsync(), await second.Response.Content.ReadAsStringAsync());
        Assert.Equal(1L, await factory.CountRowsAsync("email_submissions"));
        Assert.Equal("not_accepted", await ScalarAsync("SELECT status FROM email_submissions"));
        Assert.Empty(await BlobNamesAsync());
    }

    [Fact]
    public async Task TenConcurrentIdenticalRequests_ProduceOneSubmissionAndOneSetOfBlobs_WithEveryResponse202()
    {
        await factory.InsertUserProfileAsync(Alice, AliceEmail);
        var email = IntakeClient.NewEmail(AliceEmail, messageId: "<burst@mail.test>").AddPdf().AddPng();
        var client = factory.CreateClientForWorkflow();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => IntakeClient.PostAsync(client, email)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        var acknowledgements = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<IntakeAcknowledgement>()));
        Assert.Single(acknowledgements.Select(acknowledgement => acknowledgement!.SubmissionId).Distinct());
        Assert.Equal(1L, await factory.CountRowsAsync("email_submissions"));
        Assert.Equal(2L, await factory.CountRowsAsync("email_attachments"));
        var expectedBlobs = (await ScalarAsync("SELECT string_agg(submission_id || '/' || id, ',' ORDER BY submission_id || '/' || id) FROM email_attachments"))!.Split(',');
        Assert.Equal(expectedBlobs, (await BlobNamesAsync()).Order());
    }

    [Fact]
    public async Task AFailedMessageThatIsSentAgain_IsQueuedAgainAndTheNewAttemptSucceeds()
    {
        factory.Extractor.Reset();
        await factory.InsertUserProfileAsync(Alice, AliceEmail);
        var email = IntakeClient.NewEmail(AliceEmail, messageId: "<retry@mail.test>");
        var (_, original) = await PostOkAsync(email);
        factory.Extractor.EnqueueFailure();
        factory.Extractor.EnqueueFailure();
        factory.Extractor.EnqueueFailure();
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        var processor = factory.CreateProcessor(clock);
        Assert.True(await processor.ProcessOnceAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await processor.ProcessOnceAsync(CancellationToken.None));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await processor.ProcessOnceAsync(CancellationToken.None));
        Assert.Equal("failed", (await factory.ReadSubmissionStateAsync(original.SubmissionId)).Status);

        var (_, resent) = await PostOkAsync(email);

        Assert.Equal(1L, await factory.CountRowsAsync("email_submissions"));
        var queued = await factory.ReadSubmissionStateAsync(resent.SubmissionId);
        Assert.Equal("queued", queued.Status);
        Assert.Equal(0, queued.Attempts);
        factory.Extractor.Enqueue(ServiceRecordResult());
        Assert.True(await processor.ProcessOnceAsync(CancellationToken.None));
        Assert.Equal("ready", (await factory.ReadSubmissionStateAsync(resent.SubmissionId)).Status);
        Assert.Equal(1L, await factory.CountRowsAsync("maintenance_drafts"));
    }

    [Fact]
    public async Task AFailedMessageSentAgainWithAttachments_ReplacesTheOldFilesAndBlobs()
    {
        await factory.InsertUserProfileAsync(Alice, AliceEmail);
        var (_, original) = await PostOkAsync(IntakeClient.NewEmail(AliceEmail, messageId: "<retry2@mail.test>").AddPdf());
        var oldBlobs = await BlobNamesAsync();
        Assert.Single(oldBlobs);
        await factory.ExecuteScalarAsync("UPDATE email_submissions SET status = 'failed', attempt_count = 3, failure_reason = 'service_error' WHERE id = @id", ("id", original.SubmissionId));

        var (_, resent) = await PostOkAsync(IntakeClient.NewEmail(AliceEmail, messageId: "<retry2@mail.test>").AddPng("new.png"));

        Assert.Equal(1L, await factory.CountRowsAsync("email_submissions"));
        Assert.Equal("new.png", await ScalarAsync("SELECT string_agg(file_name, ',') FROM email_attachments"));
        var blobs = await BlobNamesAsync();
        Assert.Single(blobs);
        Assert.DoesNotContain(oldBlobs[0], blobs);
        Assert.StartsWith($"{resent.SubmissionId}/", blobs[0]);
        Assert.Equal("queued", await ScalarAsync($"SELECT status FROM email_submissions WHERE id = '{resent.SubmissionId}'"));
    }

    [Fact]
    public async Task ARepeatedBody_DoesNotChangeAnExistingDraftOrSendASecondNotification()
    {
        factory.Extractor.Reset();
        await factory.InsertUserProfileAsync(Alice, AliceEmail);
        var email = IntakeClient.NewEmail(AliceEmail, messageId: "<done@mail.test>");
        var (_, original) = await PostOkAsync(email);
        factory.Extractor.Enqueue(ServiceRecordResult());
        Assert.True(await factory.CreateProcessor().ProcessOnceAsync(CancellationToken.None));
        var draftBefore = await ScalarAsync("SELECT id || '|' || state || '|' || proposal::text FROM maintenance_drafts");

        var (_, repeated) = await PostOkAsync(email);

        Assert.Equal(original.SubmissionId, repeated.SubmissionId);
        Assert.Equal(1L, await factory.CountRowsAsync("maintenance_drafts"));
        Assert.Equal(1L, await factory.CountRowsAsync("notifications"));
        Assert.Equal(draftBefore, await ScalarAsync("SELECT id || '|' || state || '|' || proposal::text FROM maintenance_drafts"));
        Assert.Equal("ready", (await factory.ReadSubmissionStateAsync(original.SubmissionId)).Status);
    }

    [Fact]
    public async Task InvalidBody_Returns400NamingEachField_AndStoresNothing()
    {
        await factory.InsertUserProfileAsync(Alice, AliceEmail);
        var email = new IntakeEmailRequest
        {
            MessageId = string.Empty,
            From = "not-an-address",
            Subject = new string('s', 501),
            Attachments = [new IntakeAttachmentRequest { FileName = "a.pdf", ContentBase64 = "%%% not base64" }],
        };

        var response = await IntakeClient.PostAsync(factory.CreateClientForWorkflow(), email);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("MessageId", problem!.Errors.Keys);
        Assert.Contains("From", problem.Errors.Keys);
        Assert.Contains("Subject", problem.Errors.Keys);
        Assert.Contains("Attachments[0].ContentBase64", problem.Errors.Keys);
        Assert.Equal(0L, await factory.CountRowsAsync("email_submissions"));
        Assert.Empty(await BlobNamesAsync());
    }

    [Fact]
    public async Task OversizeBody_Returns413Problem_AndStoresNothing()
    {
        var filler = new string('a', 22_100_000);
        var bytes = Encoding.UTF8.GetBytes($$"""{"messageId":"<big@mail.test>","from":"{{AliceEmail}}","bodyText":"{{filler}}"}""");
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentLength = bytes.Length;

        var response = await factory.CreateClientForWorkflow().PostAsync(IntakeClient.Url, content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(0L, await factory.CountRowsAsync("email_submissions"));
    }

    private HttpClient ClientWithStore(Func<IServiceProvider, IIntakeBlobStore> create) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IIntakeBlobStore>();
            services.AddSingleton(create);
        })).CreateWorkflowClient();

    [Fact]
    public async Task StorageOutage_Returns503WithRetryAfter_AndStoresNoSubmission()
    {
        await factory.InsertUserProfileAsync(Alice, AliceEmail);
        using var client = ClientWithStore(_ => new FlakyIntakeStore(null, failOnUpload: 1));

        var response = await IntakeClient.PostAsync(client, IntakeClient.NewEmail(AliceEmail).AddPdf());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.RetryAfter is not null);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(0L, await factory.CountRowsAsync("email_submissions"));
    }

    [Fact]
    public async Task OutageAfterTheFirstUpload_DeletesTheBlobAlreadyWritten()
    {
        await factory.InsertUserProfileAsync(Alice, AliceEmail);
        using var client = ClientWithStore(sp => new FlakyIntakeStore(
            new AzureBlobIntakeStore(sp.GetRequiredService<BlobServiceClient>(), factory.IntakeContainerName), failOnUpload: 2));

        var response = await IntakeClient.PostAsync(client, IntakeClient.NewEmail(AliceEmail).AddPdf().AddPng());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(await BlobNamesAsync());
        Assert.Equal(0L, await factory.CountRowsAsync("email_submissions"));
    }

    // Delegates to the real store but fails the nth upload with the storage-unavailable error.
    private sealed class FlakyIntakeStore(IIntakeBlobStore? inner, int failOnUpload) : IIntakeBlobStore
    {
        private int uploads;

        public Task EnsureContainerAsync(CancellationToken cancellationToken) => inner?.EnsureContainerAsync(cancellationToken) ?? Task.CompletedTask;

        public Task UploadAsync(Guid submissionId, Guid attachmentId, Stream content, string contentType, CancellationToken cancellationToken) =>
            Interlocked.Increment(ref uploads) == failOnUpload
                ? throw new IntakeStorageUnavailableException("The test made storage fail.", null)
                : inner!.UploadAsync(submissionId, attachmentId, content, contentType, cancellationToken);

        public Task<Stream?> OpenReadAsync(Guid submissionId, Guid attachmentId, CancellationToken cancellationToken) =>
            inner!.OpenReadAsync(submissionId, attachmentId, cancellationToken);

        public Task DeleteAsync(Guid submissionId, Guid attachmentId, CancellationToken cancellationToken) =>
            inner?.DeleteAsync(submissionId, attachmentId, cancellationToken) ?? Task.CompletedTask;

        public Task DeleteSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) =>
            inner?.DeleteSubmissionAsync(submissionId, cancellationToken) ?? Task.CompletedTask;

        public Task<IReadOnlyList<string>> ListBlobNamesOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
            inner?.ListBlobNamesOlderThanAsync(cutoff, cancellationToken) ?? Task.FromResult<IReadOnlyList<string>>([]);
    }
}

public static class IntakeFactoryExtensions
{
    // A workflow client for a derived host (WithWebHostBuilder) of ApiFactory.
    public static HttpClient CreateWorkflowClient(this Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> derived)
    {
        var client = derived.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwt.Create(
                scope: null,
                includeUserClaims: false,
                claims: new Dictionary<string, object>
                {
                    ["oid"] = TestJwt.WorkflowAppId,
                    ["azp"] = TestJwt.WorkflowAppId,
                    ["roles"] = new[] { TestJwt.EmailIngestRole },
                }));
        return client;
    }
}

// Rate limiting is per host, so this class gets its own factory and the other intake tests never share its window.
public class IntakeRateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The121stRequestInAMinute_Returns429WithRetryAfter()
    {
        var client = factory.CreateClientForWorkflow();

        for (var i = 0; i < 120; i++)
        {
            var accepted = await IntakeClient.PostAsync(client, IntakeClient.NewEmail("stranger@nowhere.test"));
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        var limited = await IntakeClient.PostAsync(client, IntakeClient.NewEmail("stranger@nowhere.test"));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        Assert.Equal(120L, await factory.CountRowsAsync("email_submissions"));
    }
}

// FR-029: logs carry ids, counts, statuses, and durations only, never the sender, subject, text, file names, or attachment content.
[Collection("Logging")]
public class IntakeLoggingTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly string[] Markers = ["QZSENDER", "QZSUBJECT", "QZBODY", "QZFILE", "QZBYTES", "QZMSGID", "QZSTRANGER"];

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(' ', pairs.Select(pair => $"{pair.Key}={pair.Value}")) : string.Empty;
            Entries.Enqueue($"{logLevel} {formatter(state, exception)} {values} {exception}");
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task EveryIntakePath_KeepsEmailContentOutOfTheLogs()
    {
        await factory.InsertUserProfileAsync("log-user", "qzsender@contoso.test");
        var provider = new CapturingLoggerProvider();
        using var logged = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(provider);
        }));
        var client = logged.CreateWorkflowClient();
        // Proves the host writes to the capturing provider, so an empty result below could not hide a leak.
        logged.Services.GetRequiredService<ILoggerFactory>().CreateLogger("IntakeLoggingTests").LogInformation("capture check");

        var marked = "%PDF-1.4 QZBYTES"u8.ToArray();
        var matched = IntakeClient.NewEmail("qzsender@contoso.test", "<QZMSGID-1@mail.test>", "QZSUBJECT", "QZBODY")
            .AddAttachment("QZFILE.pdf", "application/pdf", marked)
            .AddAttachment("QZFILE-fake.pdf", "application/pdf", "QZBYTES not a pdf"u8.ToArray());
        var base64 = Convert.ToBase64String(marked);
        await IntakeClient.PostAsync(client, matched);
        await IntakeClient.PostAsync(client, matched);
        await IntakeClient.PostAsync(client, IntakeClient.NewEmail("qzstranger@nowhere.test", "<QZMSGID-2@mail.test>", "QZSUBJECT", "QZBODY").AddAttachment("QZFILE.pdf", "application/pdf", marked));
        await IntakeClient.PostAsync(client, new IntakeEmailRequest
        {
            MessageId = "<QZMSGID-3@mail.test>",
            From = "QZSENDER-not-an-address",
            Subject = "QZSUBJECT",
            BodyText = "QZBODY",
            Attachments = [new IntakeAttachmentRequest { FileName = "QZFILE.pdf", ContentBase64 = "QZBYTES %%%" }],
        });

        Assert.Contains(provider.Entries, entry => entry.Contains("capture check"));
        Assert.DoesNotContain(provider.Entries, entry => entry.Contains(base64) || Markers.Any(marker => entry.Contains(marker, StringComparison.OrdinalIgnoreCase)));
    }
}
