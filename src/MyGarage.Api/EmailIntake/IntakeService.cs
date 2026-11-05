using System.Text.Json;
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
    private sealed record SkippedEntry(string FileName, string Reason);

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
        var prepared = IntakeAttachments.Prepare(submissionId, ownerId, now, ReadFiles(request));
        var stored = prepared.Stored;
        var skipped = prepared.Skipped.Select(entry => new SkippedEntry(entry.FileName, entry.Reason)).ToList();

        try
        {
            await IntakeAttachments.UploadAsync(blobs, submissionId, stored, cancellationToken);

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
                await IntakeAttachments.DeleteBlobsAsync(blobs, submissionId, stored.Count, logger);
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
            await IntakeAttachments.DeleteBlobsAsync(blobs, submissionId, stored.Count, logger);
            throw;
        }
    }

    private static IntakeAcknowledgement Acknowledge(Guid submissionId) => new() { SubmissionId = submissionId };

    // Non-inline attachments are typed by their bytes: PDFs and images are stored, anything else is skipped and listed.
    private static List<IntakeFile> ReadFiles(IntakeEmailRequest request) =>
    [
        .. request.Attachments
            .Where(attachment => !attachment.IsInline)
            .Select(attachment => new IntakeFile(attachment.FileName, Convert.FromBase64String(attachment.ContentBase64!))),
    ];

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
}
