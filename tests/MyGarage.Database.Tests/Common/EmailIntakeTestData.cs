using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Dapper;
using Npgsql;

namespace MyGarage.Database.Tests.Common;

// Direct SQL seeding of the email intake tables so repository tests do not depend on the repositories under test.
// Vehicles come from TestData and the vehicle repository; blobs use AzuriteFixture.Service.
public static class EmailIntakeTestData
{
    // Smallest byte sequences with a real PDF or PNG signature.
    public static readonly byte[] PdfBytes = [.. "%PDF-1.4\n1 0 obj\n<< >>\nendobj\ntrailer\n<< >>\n%%EOF\n"u8.ToArray()];

    public static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    // Returns the owner id.
    public static async Task<string> InsertUserProfileAsync(
        NpgsqlDataSource dataSource,
        string ownerId,
        string? email = null,
        string displayName = "Test User",
        DateTimeOffset? at = null)
    {
        await ExecuteAsync(
            dataSource,
            "INSERT INTO user_profiles (owner_id, email, display_name, first_seen_at, last_seen_at) VALUES (@ownerId, @email, @displayName, @at, @at)",
            new { ownerId, email, displayName, at = at ?? DateTimeOffset.UtcNow });
        return ownerId;
    }

    public static async Task<Guid> InsertSubmissionAsync(
        NpgsqlDataSource dataSource,
        string? messageId = null,
        string status = "queued",
        string? ownerId = "owner-a",
        string? sender = "owner@example.com",
        string? subject = "Fwd: invoice",
        string? bodyText = "Oil change today",
        DateTimeOffset? arrivedAt = null,
        DateTimeOffset? nextAttemptAt = null,
        DateTimeOffset? lockedUntil = null,
        int attemptCount = 0,
        string skippedAttachmentsJson = "[]",
        DateTimeOffset? contentPurgedAt = null)
    {
        var id = Guid.CreateVersion7();
        var arrived = arrivedAt ?? DateTimeOffset.UtcNow;
        await ExecuteAsync(
            dataSource,
            "INSERT INTO email_submissions (id, message_id, status, arrived_at, received_at, owner_id, sender, subject, body_text, attempt_count, next_attempt_at, locked_until, skipped_attachments, content_purged_at) " +
            "VALUES (@id, @messageId, @status, @arrived, @arrived, @ownerId, @sender, @subject, @bodyText, @attemptCount, @nextAttemptAt, @lockedUntil, @skippedAttachmentsJson::jsonb, @contentPurgedAt)",
            new
            {
                id,
                messageId = messageId ?? $"<{id:N}@mail.test>",
                status,
                arrived,
                ownerId,
                sender,
                subject,
                bodyText,
                attemptCount,
                nextAttemptAt = nextAttemptAt ?? arrived,
                lockedUntil,
                skippedAttachmentsJson,
                contentPurgedAt,
            });
        return id;
    }

    public static async Task<Guid> InsertAttachmentAsync(
        NpgsqlDataSource dataSource,
        Guid submissionId,
        string ownerId = "owner-a",
        string fileName = "invoice.pdf",
        string contentType = "application/pdf",
        int sizeBytes = 1024,
        string? sha256 = null,
        DateTimeOffset? createdAt = null)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(
            dataSource,
            "INSERT INTO email_attachments (id, submission_id, owner_id, file_name, content_type, size_bytes, sha256, created_at) " +
            "VALUES (@id, @submissionId, @ownerId, @fileName, @contentType, @sizeBytes, @sha256, @createdAt)",
            new { id, submissionId, ownerId, fileName, contentType, sizeBytes, sha256 = sha256 ?? new string('a', 64), createdAt = createdAt ?? DateTimeOffset.UtcNow });
        return id;
    }

    public static async Task<Guid> InsertDraftAsync(
        NpgsqlDataSource dataSource,
        string ownerId,
        Guid submissionId,
        string state = "needs_review",
        string? kind = "service_record",
        Guid? vehicleId = null,
        Guid? attachmentId = null,
        string vehicleMatch = "none",
        decimal? confidence = 0.80m,
        string proposalJson = "{}",
        string? notes = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? decidedAt = null,
        string? createdRecordKind = null,
        Guid? createdRecordId = null)
    {
        var id = Guid.CreateVersion7();
        var created = createdAt ?? DateTimeOffset.UtcNow;
        await ExecuteAsync(
            dataSource,
            "INSERT INTO maintenance_drafts (id, owner_id, submission_id, attachment_id, state, kind, vehicle_id, vehicle_match, confidence, proposal, notes, created_at, expires_at, decided_at, created_record_kind, created_record_id) " +
            "VALUES (@id, @ownerId, @submissionId, @attachmentId, @state, @kind, @vehicleId, @vehicleMatch, @confidence, @proposalJson::jsonb, @notes, @created, @expires, @decidedAt, @createdRecordKind, @createdRecordId)",
            new
            {
                id,
                ownerId,
                submissionId,
                attachmentId,
                state,
                kind,
                vehicleId,
                vehicleMatch,
                confidence,
                proposalJson,
                notes,
                created,
                expires = expiresAt ?? created.AddDays(30),
                decidedAt,
                createdRecordKind,
                createdRecordId,
            });
        return id;
    }

    public static async Task<Guid> InsertNotificationAsync(
        NpgsqlDataSource dataSource,
        string ownerId,
        Guid? draftId = null,
        string title = "New draft service record ready to review",
        string kind = "draft_ready",
        DateTimeOffset? createdAt = null,
        DateTimeOffset? readAt = null)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(
            dataSource,
            "INSERT INTO notifications (id, owner_id, kind, draft_id, title, created_at, read_at) VALUES (@id, @ownerId, @kind, @draftId, @title, @createdAt, @readAt)",
            new { id, ownerId, kind, draftId, title, createdAt = createdAt ?? DateTimeOffset.UtcNow, readAt });
        return id;
    }

    public static async Task<Guid> InsertVehicleDocumentAsync(
        NpgsqlDataSource dataSource,
        Guid vehicleId,
        string fileName = "invoice.pdf",
        string contentType = "application/pdf",
        int sizeBytes = 1024,
        string? sourceKind = null,
        Guid? sourceRecordId = null,
        DateTimeOffset? createdAt = null,
        string createdBy = "seed")
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(
            dataSource,
            "INSERT INTO vehicle_documents (id, vehicle_id, file_name, content_type, size_bytes, source_kind, source_record_id, created_at, created_by) " +
            "VALUES (@id, @vehicleId, @fileName, @contentType, @sizeBytes, @sourceKind, @sourceRecordId, @createdAt, @createdBy)",
            new { id, vehicleId, fileName, contentType, sizeBytes, sourceKind, sourceRecordId, createdAt = createdAt ?? DateTimeOffset.UtcNow, createdBy });
        return id;
    }

    // Creates the container privately when needed and uploads one blob.
    public static async Task UploadBlobAsync(BlobServiceClient service, string containerName, string blobName, byte[] content, string contentType)
    {
        var container = service.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None);
        await container.GetBlobClient(blobName).UploadAsync(
            new BinaryData(content),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } });
    }

    public static async Task<bool> BlobExistsAsync(BlobServiceClient service, string containerName, string blobName) =>
        await service.GetBlobContainerClient(containerName).GetBlobClient(blobName).ExistsAsync();

    private static async Task ExecuteAsync(NpgsqlDataSource dataSource, string sql, object values)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, values);
    }
}
