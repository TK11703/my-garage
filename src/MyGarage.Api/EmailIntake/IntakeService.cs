using System.Security.Cryptography;
using System.Text.Json;
using MyGarage.Api.Common;
using MyGarage.Database.EmailIntake;
using MyGarage.Database.Users;
using MyGarage.Shared.EmailIntake;

namespace MyGarage.Api.EmailIntake;

// Accepts one transformed email from the trusted workflow. This is the only place an owner is found from a sender address
// (constitution v1.3.0 trusted service intake): the match only decides whether a pending submission is stored; nothing is
// created for the owner until they approve a draft, and the caller always gets the same acknowledgement.
public sealed class IntakeService(
    UserProfileRepository profiles,
    EmailSubmissionRepository submissions,
    IIntakeBlobStore blobs,
    TimeProvider timeProvider,
    ILogger<IntakeService> logger)
{
    private const string UnsupportedType = "unsupported_type";

    private sealed record SkippedAttachment(string FileName, string Reason);

    private sealed record StoredAttachment(EmailAttachmentRecord Record, byte[] Content);

    // The request has already passed IntakeRequestValidator.
    public async Task<IntakeAcknowledgement> AcceptAsync(IntakeEmailRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var messageId = request.MessageId!;
        var sender = request.From!.Trim();

        var owners = await profiles.FindOwnersByEmailAsync(sender, cancellationToken);
        if (owners.Count != 1)
        {
            var notAccepted = await submissions.AddNotAcceptedAsync(Guid.CreateVersion7(), messageId, now, cancellationToken);
            logger.LogInformation("Intake recorded submission {SubmissionId} as {Outcome}", notAccepted.SubmissionId, "not_accepted");
            return Acknowledge(notAccepted.SubmissionId);
        }

        var ownerId = owners[0];
        var submissionId = Guid.CreateVersion7();
        var (stored, skipped) = ReadAttachments(request, submissionId, ownerId, now);

        try
        {
            foreach (var attachment in stored)
            {
                await blobs.UploadAsync(
                    submissionId, attachment.Record.Id, new MemoryStream(attachment.Content, writable: false), attachment.Record.ContentType, cancellationToken);
            }

            var result = await submissions.AddReceivedAsync(
                new EmailSubmissionRecord
                {
                    Id = submissionId,
                    MessageId = messageId,
                    ArrivedAt = now,
                    ReceivedAt = request.ReceivedAt ?? now,
                    OwnerId = ownerId,
                    Sender = sender,
                    Subject = request.Subject,
                    BodyText = request.BodyText,
                    SkippedAttachments = JsonSerializer.Serialize(skipped, JsonSerializerOptions.Web),
                },
                [.. stored.Select(attachment => attachment.Record)],
                cancellationToken);

            // Only this request's own blobs are ever deleted on a lost race: the winner's rows point at theirs.
            if (result.Status == AddSubmissionStatus.Duplicate)
            {
                await DeleteBlobsAsync(submissionId, stored.Count);
            }
            else if (result.Status == AddSubmissionStatus.Requeued)
            {
                await DeleteReplacedBlobsAsync(result);
            }

            logger.LogInformation(
                "Intake {Outcome} submission {SubmissionId} with {StoredAttachments} stored and {SkippedAttachments} skipped attachments",
                result.Status switch
                {
                    AddSubmissionStatus.Added => "queued",
                    AddSubmissionStatus.Requeued => "requeued",
                    _ => "repeated",
                },
                result.SubmissionId,
                stored.Count,
                skipped.Count);
            return Acknowledge(result.SubmissionId);
        }
        catch
        {
            await DeleteBlobsAsync(submissionId, stored.Count);
            throw;
        }
    }

    private static IntakeAcknowledgement Acknowledge(Guid submissionId) => new() { SubmissionId = submissionId };

    // Non-inline attachments are typed by their bytes: PDFs and images are stored, anything else is skipped and listed.
    private static (List<StoredAttachment> Stored, List<SkippedAttachment> Skipped) ReadAttachments(
        IntakeEmailRequest request, Guid submissionId, string ownerId, DateTimeOffset now)
    {
        var stored = new List<StoredAttachment>();
        var skipped = new List<SkippedAttachment>();

        foreach (var attachment in request.Attachments.Where(attachment => !attachment.IsInline))
        {
            var fileName = SafeFileName(attachment.FileName);
            var content = Convert.FromBase64String(attachment.ContentBase64!);
            if (FileSignature.Detect(content) is not { } contentType)
            {
                skipped.Add(new SkippedAttachment(fileName, UnsupportedType));
                continue;
            }

            stored.Add(new StoredAttachment(
                new EmailAttachmentRecord
                {
                    Id = Guid.CreateVersion7(),
                    SubmissionId = submissionId,
                    OwnerId = ownerId,
                    FileName = fileName,
                    ContentType = contentType,
                    SizeBytes = content.Length,
                    Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
                    CreatedAt = now,
                },
                content));
        }

        return (stored, skipped);
    }

    // A display name only: any path and control characters are dropped, and it is never used as a storage path.
    private static string SafeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        name = new string([.. name.Where(character => !char.IsControl(character))]).Trim();
        return name.Length == 0 ? "attachment" : name;
    }

    // The requeued submission's previous files, removed after the commit; a failure only leaves unreferenced blobs for the orphan sweep.
    private async Task DeleteReplacedBlobsAsync(AddSubmissionResult result)
    {
        if (result.PreviousSubmissionId is not { } previousId)
        {
            return;
        }

        foreach (var attachmentId in result.RemovedAttachmentIds)
        {
            try
            {
                await blobs.DeleteAsync(previousId, attachmentId, CancellationToken.None);
            }
            catch (IntakeStorageUnavailableException ex)
            {
                logger.LogWarning(ex, "Could not delete the replaced attachment file {AttachmentId} of submission {SubmissionId}", attachmentId, previousId);
            }
        }
    }

    // Best effort: a blob that cannot be removed now is private and unreferenced, and the orphan sweep removes it later.
    private async Task DeleteBlobsAsync(Guid submissionId, int storedCount)
    {
        if (storedCount == 0)
        {
            return;
        }

        try
        {
            await blobs.DeleteSubmissionAsync(submissionId, CancellationToken.None);
        }
        catch (IntakeStorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Could not delete the attachment files of unsaved submission {SubmissionId}", submissionId);
        }
    }
}
