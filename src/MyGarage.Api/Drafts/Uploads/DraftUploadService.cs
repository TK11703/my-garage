using MyGarage.Api.EmailIntake;
using MyGarage.Database.EmailIntake;
using MyGarage.Database.Vehicles;
using MyGarage.Shared.Drafts;

namespace MyGarage.Api.Drafts.Uploads;

public enum DraftUploadOutcome
{
    Accepted,
    Refused,
    VehicleNotFound,
    VehicleArchived,
}

// Errors are keyed files[i]; their text never contains a file name or any file content.
public sealed record DraftUploadResult(DraftUploadOutcome Outcome, DraftUploadResultDto? Value = null, Dictionary<string, string[]>? Errors = null);

// Queues uploaded files exactly as an email's attachments are queued: a submission owned by the caller, whose files the
// background processor reads into ordinary drafts. Nothing is created for the owner until they approve a draft.
public sealed class DraftUploadService(
    EmailSubmissionRepository submissions,
    VehicleRepository vehicles,
    IIntakeBlobStore blobs,
    TimeProvider timeProvider,
    ILogger<DraftUploadService> logger)
{
    public async Task<DraftUploadResult> UploadAsync(string ownerId, IFormFileCollection files, Guid? vehicleId, CancellationToken cancellationToken)
    {
        // The vehicle must be the caller's before anything is read or stored; another user's vehicle is indistinguishable from an unknown one.
        if (vehicleId is { } chosen)
        {
            var vehicle = await vehicles.GetByIdAsync(ownerId, chosen, cancellationToken);
            if (vehicle is null)
            {
                return new DraftUploadResult(DraftUploadOutcome.VehicleNotFound);
            }

            if (vehicle.ArchivedAt is not null)
            {
                return new DraftUploadResult(DraftUploadOutcome.VehicleArchived);
            }
        }

        var now = timeProvider.GetUtcNow();
        var submissionId = Guid.CreateVersion7();

        var inputs = new List<IntakeFile>(files.Count);
        foreach (var file in files)
        {
            inputs.Add(new IntakeFile(file.FileName, await ReadAsync(file, cancellationToken)));
        }

        var prepared = IntakeAttachments.Prepare(submissionId, ownerId, now, inputs);
        if (prepared.Stored.Count == 0)
        {
            logger.LogInformation("Upload refused all {Refused} files", prepared.Skipped.Count);
            return new DraftUploadResult(DraftUploadOutcome.Refused, Errors: ToErrors(prepared.Skipped));
        }

        try
        {
            await IntakeAttachments.UploadAsync(blobs, submissionId, prepared.Stored, cancellationToken);
            await submissions.AddReceivedAsync(
                new EmailSubmissionRecord
                {
                    Id = submissionId,
                    MessageId = $"upload:{submissionId}",
                    ArrivedAt = now,
                    ReceivedAt = now,
                    OwnerId = ownerId,
                    Origin = "upload",
                    PreselectedVehicleId = vehicleId,
                },
                [.. prepared.Stored.Select(attachment => attachment.Record)],
                cancellationToken);
        }
        catch
        {
            await IntakeAttachments.DeleteBlobsAsync(blobs, submissionId, prepared.Stored.Count, logger);
            throw;
        }

        logger.LogInformation(
            "Upload queued submission {SubmissionId} with {Accepted} accepted and {Refused} refused files", submissionId, prepared.Stored.Count, prepared.Skipped.Count);
        return new DraftUploadResult(
            DraftUploadOutcome.Accepted,
            new DraftUploadResultDto
            {
                SubmissionId = submissionId,
                AcceptedCount = prepared.Stored.Count,
                Refused = [.. prepared.Skipped.Select(skipped => new DraftUploadRefusalDto { Index = skipped.Index, Reason = skipped.Reason })],
            });
    }

    // At most one byte past the file limit is read: enough to tell the file is too large without holding all of it.
    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        var limit = DraftUploadLimits.MaxFileBytes + 1;
        var chunk = new byte[81_920];
        while (buffer.Length < limit)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - buffer.Length)), cancellationToken);
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static Dictionary<string, string[]> ToErrors(IEnumerable<SkippedAttachment> skipped) =>
        skipped.ToDictionary(entry => $"files[{entry.Index}]", entry => new[] { MessageFor(entry.Reason) });

    private static string MessageFor(string reason) =>
        reason switch
        {
            DraftUploadLimits.Empty => "The file is empty.",
            DraftUploadLimits.TooLarge => $"The file must be {DraftUploadLimits.MaxFileBytes / 1_048_576} MB or smaller.",
            _ => "The file must be a PDF, JPEG, PNG, or WebP.",
        };
}
