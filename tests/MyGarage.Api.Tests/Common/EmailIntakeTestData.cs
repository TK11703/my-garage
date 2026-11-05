using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyGarage.Api.EmailIntake;
using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Database.Drafts;
using MyGarage.Database.EmailIntake;
using MyGarage.Shared.EmailIntake;

namespace MyGarage.Api.Tests.Common;

// Builds intake requests and posts them as the workflow (use ApiFactory.CreateClientForWorkflow()).
public static class IntakeClient
{
    public const string Url = "/api/intake/emails";

    // Smallest byte sequences that FileSignature accepts; only the leading bytes matter.
    public static readonly byte[] PdfBytes = [.. "%PDF-1.4\n1 0 obj\n<< >>\nendobj\ntrailer\n<< >>\n%%EOF\n"u8.ToArray()];

    public static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public static IntakeEmailRequest NewEmail(
        string from = "alice@contoso.test",
        string? messageId = null,
        string? subject = "Fwd: Your service invoice",
        string? bodyText = "Forwarding the invoice from today's oil change.",
        DateTimeOffset? receivedAt = null) =>
        new()
        {
            MessageId = messageId ?? $"<{Guid.NewGuid():N}@mail.test>",
            ReceivedAt = receivedAt ?? DateTimeOffset.UtcNow,
            From = from,
            Subject = subject,
            BodyText = bodyText,
        };

    // Requests are immutable, so every Add returns a copy with the extra attachment.
    public static IntakeEmailRequest AddRawAttachment(this IntakeEmailRequest email, IntakeAttachmentRequest attachment) =>
        new()
        {
            MessageId = email.MessageId,
            ReceivedAt = email.ReceivedAt,
            From = email.From,
            Subject = email.Subject,
            BodyText = email.BodyText,
            Attachments = [.. email.Attachments, attachment],
        };

    public static IntakeEmailRequest AddAttachment(this IntakeEmailRequest email, string fileName, string contentType, byte[] content, bool isInline = false) =>
        email.AddRawAttachment(new IntakeAttachmentRequest
        {
            FileName = fileName,
            ContentType = contentType,
            IsInline = isInline,
            ContentBase64 = Convert.ToBase64String(content),
        });

    public static IntakeEmailRequest AddPdf(this IntakeEmailRequest email, string fileName = "invoice.pdf", bool isInline = false) =>
        email.AddAttachment(fileName, "application/pdf", PdfBytes, isInline);

    public static IntakeEmailRequest AddPng(this IntakeEmailRequest email, string fileName = "receipt.png", bool isInline = false) =>
        email.AddAttachment(fileName, "image/png", PngBytes, isInline);

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, IntakeEmailRequest email) =>
        client.PostAsJsonAsync(Url, email);
}
// Direct database seeding and counting for the email intake tables.
public static class EmailIntakeTestData
{
    public static async Task InsertUserProfileAsync(this ApiFactory factory, string ownerId, string? email, string displayName = "Test User") =>
        await factory.ExecuteScalarAsync(
            "INSERT INTO user_profiles (owner_id, email, display_name, first_seen_at, last_seen_at) VALUES (@ownerId, @email, @displayName, now(), now())",
            ("ownerId", ownerId), ("email", email), ("displayName", displayName));

    public static async Task<long> CountRowsAsync(this ApiFactory factory, string table)
    {
        Assert.All(table, character => Assert.True(char.IsAsciiLetterLower(character) || character == '_', "Table names are test constants."));
        return (long)(await factory.ExecuteScalarAsync($"SELECT count(*) FROM {table}"))!;
    }

    // Seeds a vehicle with the fields vehicle matching reads; returns its id.
    public static async Task<Guid> InsertVehicleAsync(
        this ApiFactory factory, string ownerId, string make = "Ford", string model = "F-150", int? year = null, string? vin = null)
    {
        var id = Guid.NewGuid();
        await factory.ExecuteScalarAsync(
            "INSERT INTO vehicles (id, owner_id, make, model, year, vin, mileage, color, doors, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @ownerId, @make, @model, @year, @vin, 1200, 'Red', 4, now(), now(), @ownerId, @ownerId)",
            ("id", id), ("ownerId", ownerId), ("make", make), ("model", model), ("year", year), ("vin", vin));
        return id;
    }

    // Seeds a submission directly so a test controls its status, attempts, and due time; returns its id.
    public static async Task<Guid> InsertSubmissionAsync(
        this ApiFactory factory,
        string ownerId,
        string status = "queued",
        string? subject = "Fwd: invoice",
        string? bodyText = "Oil change today",
        DateTimeOffset? nextAttemptAt = null,
        DateTimeOffset? lockedUntil = null,
        int attemptCount = 0,
        string skippedAttachmentsJson = "[]",
        string origin = "email",
        Guid? preselectedVehicleId = null)
    {
        var id = Guid.CreateVersion7();
        var due = nextAttemptAt ?? DateTimeOffset.UtcNow.AddMinutes(-1);
        await factory.ExecuteScalarAsync(
            "INSERT INTO email_submissions (id, message_id, status, arrived_at, received_at, owner_id, sender, subject, body_text, attempt_count, next_attempt_at, locked_until, skipped_attachments, origin, preselected_vehicle_id) " +
            "VALUES (@id, @messageId, @status, @due, @due, @ownerId, 'sender@contoso.test', @subject, @bodyText, @attemptCount, @due, @lockedUntil, @skipped::jsonb, @origin, @preselected)",
            ("id", id), ("messageId", $"<{id:N}@mail.test>"), ("status", status), ("due", due), ("ownerId", ownerId), ("subject", subject),
            ("bodyText", bodyText), ("attemptCount", (short)attemptCount), ("lockedUntil", lockedUntil), ("skipped", skippedAttachmentsJson),
            ("origin", origin), ("preselected", preselectedVehicleId));
        return id;
    }

    // Builds a processor over this factory's repositories and fake extractor with its own clock; the host's loop is off in tests.
    public static SubmissionProcessor CreateProcessor(this ApiFactory factory, TimeProvider? timeProvider = null, ILogger<SubmissionProcessor>? logger = null) =>
        new(
            factory.Services.GetRequiredService<EmailSubmissionRepository>(),
            factory.Services.GetRequiredService<MaintenanceDraftRepository>(),
            factory.Services.GetRequiredService<IIntakeBlobStore>(),
            factory.Extractor,
            timeProvider ?? TimeProvider.System,
            Options.Create(new SubmissionProcessingOptions()),
            logger ?? NullLogger<SubmissionProcessor>.Instance);

    // Status, failure reason, attempts, and next attempt of a submission.
    public static async Task<(string Status, string? FailureReason, int Attempts, DateTimeOffset NextAttemptAt)> ReadSubmissionStateAsync(this ApiFactory factory, Guid id)
    {
        var status = (string)(await factory.ExecuteScalarAsync("SELECT status FROM email_submissions WHERE id = @id", ("id", id)))!;
        var reason = (string?)await factory.ExecuteScalarAsync("SELECT failure_reason FROM email_submissions WHERE id = @id", ("id", id));
        var attempts = Convert.ToInt32(await factory.ExecuteScalarAsync("SELECT attempt_count FROM email_submissions WHERE id = @id", ("id", id)));
        var next = (DateTime)(await factory.ExecuteScalarAsync("SELECT next_attempt_at FROM email_submissions WHERE id = @id", ("id", id)))!;
        return (status, reason, attempts, new DateTimeOffset(DateTime.SpecifyKind(next, DateTimeKind.Utc)));
    }

    // Seeds an attachment row; when content is given the blob is uploaded to the intake container too. Returns its id.
    public static async Task<Guid> InsertAttachmentAsync(
        this ApiFactory factory,
        Guid submissionId,
        string ownerId,
        string fileName = "invoice.pdf",
        string contentType = "application/pdf",
        byte[]? content = null,
        string? sha256 = null)
    {
        var id = Guid.CreateVersion7();
        var data = content ?? IntakeClient.PdfBytes;
        await factory.ExecuteScalarAsync(
            "INSERT INTO email_attachments (id, submission_id, owner_id, file_name, content_type, size_bytes, sha256, created_at) VALUES (@id, @submissionId, @ownerId, @fileName, @contentType, @size, @sha, now())",
            ("id", id), ("submissionId", submissionId), ("ownerId", ownerId), ("fileName", fileName), ("contentType", contentType), ("size", data.Length),
            ("sha", sha256 ?? new string('a', 64)));
        if (content is not null)
        {
            var container = factory.IntakeContainer;
            await container.CreateIfNotExistsAsync();
            await container.GetBlobClient($"{submissionId}/{id}").UploadAsync(
                new BinaryData(content), new Azure.Storage.Blobs.Models.BlobUploadOptions { HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = contentType } });
        }

        return id;
    }

    // Seeds a draft row directly (the proposal is stored as given); returns its id.
    public static async Task<Guid> InsertDraftAsync(
        this ApiFactory factory,
        string ownerId,
        Guid submissionId,
        string state = "needs_review",
        string? kind = "service_record",
        Guid? vehicleId = null,
        Guid? attachmentId = null,
        string vehicleMatch = "none",
        string proposalJson = "{}",
        string? notes = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null,
        string? createdRecordKind = null,
        Guid? createdRecordId = null)
    {
        var id = Guid.CreateVersion7();
        var created = createdAt ?? DateTimeOffset.UtcNow;
        await factory.ExecuteScalarAsync(
            "INSERT INTO maintenance_drafts (id, owner_id, submission_id, attachment_id, state, kind, vehicle_id, vehicle_match, confidence, proposal, notes, created_at, expires_at, created_record_kind, created_record_id) " +
            "VALUES (@id, @ownerId, @submissionId, @attachmentId, @state, @kind, @vehicleId, @vehicleMatch, 0.80, @proposal::jsonb, @notes, @created, @expires, @createdRecordKind, @createdRecordId)",
            ("id", id), ("ownerId", ownerId), ("submissionId", submissionId), ("attachmentId", attachmentId), ("state", state), ("kind", kind),
            ("vehicleId", vehicleId), ("vehicleMatch", vehicleMatch), ("proposal", proposalJson), ("notes", notes), ("created", created),
            ("expires", expiresAt ?? created.AddDays(30)), ("createdRecordKind", createdRecordKind), ("createdRecordId", createdRecordId));
        return id;
    }

    public static async Task<Guid> InsertNotificationAsync(this ApiFactory factory, string ownerId, Guid? draftId = null, bool read = false, DateTimeOffset? createdAt = null)
    {
        var id = Guid.CreateVersion7();
        await factory.ExecuteScalarAsync(
            "INSERT INTO notifications (id, owner_id, kind, draft_id, title, created_at, read_at) VALUES (@id, @ownerId, 'draft_ready', @draftId, 'New draft service record ready to review', @created, @readAt)",
            ("id", id), ("ownerId", ownerId), ("draftId", draftId), ("created", createdAt ?? DateTimeOffset.UtcNow), ("readAt", read ? DateTimeOffset.UtcNow : (DateTimeOffset?)null));
        return id;
    }

    // Seeds a kept document row and uploads its file to the documents container; returns its id.
    public static async Task<Guid> InsertVehicleDocumentAsync(
        this ApiFactory factory,
        Guid vehicleId,
        byte[]? content = null,
        string fileName = "invoice.pdf",
        string contentType = "application/pdf",
        DateTimeOffset? createdAt = null,
        bool uploadBlob = true)
    {
        var id = Guid.CreateVersion7();
        var data = content ?? IntakeClient.PdfBytes;
        await factory.ExecuteScalarAsync(
            "INSERT INTO vehicle_documents (id, vehicle_id, file_name, content_type, size_bytes, source_kind, created_at, created_by) VALUES (@id, @vehicleId, @fileName, @contentType, @size, 'service_record', @created, 'seed')",
            ("id", id), ("vehicleId", vehicleId), ("fileName", fileName), ("contentType", contentType), ("size", data.Length), ("created", createdAt ?? DateTimeOffset.UtcNow));
        if (uploadBlob)
        {
            var container = factory.DocumentContainer;
            await container.CreateIfNotExistsAsync();
            await container.GetBlobClient($"{vehicleId}/{id}").UploadAsync(new BinaryData(data));
        }

        return id;
    }

    public static async Task<bool> IntakeBlobExistsAsync(this ApiFactory factory, Guid submissionId, Guid attachmentId) =>
        await factory.IntakeContainer.GetBlobClient($"{submissionId}/{attachmentId}").ExistsAsync();

    public static async Task<bool> DocumentBlobExistsAsync(this ApiFactory factory, Guid vehicleId, Guid documentId) =>
        await factory.DocumentContainer.GetBlobClient($"{vehicleId}/{documentId}").ExistsAsync();
}


public static class DerivedFactoryExtensions
{
    // A signed-in user client for a derived host (WithWebHostBuilder) of ApiFactory.
    public static HttpClient CreateUserClient(this Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> derived, string oid, string? email = null)
    {
        var claims = new Dictionary<string, object> { ["oid"] = oid };
        if (email is not null)
        {
            claims["email"] = email;
        }

        var client = derived.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestJwt.Create(claims: claims));
        return client;
    }
}

// Stands in for the AI service: answers come from a queue, every call is logged, and a call can be made to throw.
public sealed class FakeDraftExtractor : IDraftExtractor
{
    private readonly ConcurrentQueue<Func<ExtractionResult>> script = new();
    private readonly ConcurrentQueue<ExtractionInput> calls = new();
    private readonly ConcurrentQueue<(string ContentType, string FileName, byte[] Content)> documents = new();

    public bool IsEnabled { get; set; } = true;

    // Returned when the queue is empty.
    public ExtractionResult DefaultResult { get; set; } = new() { Kind = ExtractionKind.None };

    public IReadOnlyList<ExtractionInput> Calls => [.. calls];

    // The document of each call, copied when the call was made (the processor disposes the stream afterwards), in call order.
    public IReadOnlyList<(string ContentType, string FileName, byte[] Content)> Documents => [.. documents];

    public void Enqueue(ExtractionResult result) => script.Enqueue(() => result);

    public void EnqueueFailure(Exception? exception = null) =>
        script.Enqueue(() => throw (exception ?? new InvalidOperationException("The fake extractor was told to fail.")));

    public void Reset()
    {
        script.Clear();
        calls.Clear();
        documents.Clear();
        IsEnabled = true;
        DefaultResult = new ExtractionResult { Kind = ExtractionKind.None };
    }

    public Task<ExtractionResult> ExtractAsync(ExtractionInput input, CancellationToken cancellationToken)
    {
        calls.Enqueue(input);
        if (input.Document is { } document)
        {
            using var copy = new MemoryStream();
            document.Content.CopyTo(copy);
            documents.Enqueue((document.ContentType, document.FileName, copy.ToArray()));
        }

        try
        {
            return Task.FromResult(script.TryDequeue(out var next) ? next() : DefaultResult);
        }
        catch (Exception ex)
        {
            return Task.FromException<ExtractionResult>(ex);
        }
    }
}
