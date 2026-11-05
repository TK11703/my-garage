using System.Security.Cryptography;
using MyGarage.Api.Common;
using MyGarage.Database.EmailIntake;
using MyGarage.Shared.Drafts;

namespace MyGarage.Api.EmailIntake;

public sealed record IntakeFile(string? FileName, byte[] Content);

public sealed record StoredAttachment(EmailAttachmentRecord Record, byte[] Content);

// Index is the file's position in the input; FileName is the sanitized display name and Reason one of the DraftUploadLimits codes.
public sealed record SkippedAttachment(int Index, string FileName, string Reason);

public sealed record PreparedAttachments(List<StoredAttachment> Stored, List<SkippedAttachment> Skipped);

// What an email's attachments and an uploaded file have in common: typed by their bytes, sized, hashed, named for display only, and stored privately.
public static class IntakeAttachments
{
    public static PreparedAttachments Prepare(Guid submissionId, string ownerId, DateTimeOffset now, IReadOnlyList<IntakeFile> files)
    {
        var stored = new List<StoredAttachment>();
        var skipped = new List<SkippedAttachment>();

        for (var index = 0; index < files.Count; index++)
        {
            var fileName = SafeFileName(files[index].FileName);
            var content = files[index].Content;

            if (content.Length == 0)
            {
                skipped.Add(new SkippedAttachment(index, fileName, DraftUploadLimits.Empty));
                continue;
            }

            if (content.Length > DraftUploadLimits.MaxFileBytes)
            {
                skipped.Add(new SkippedAttachment(index, fileName, DraftUploadLimits.TooLarge));
                continue;
            }

            if (FileSignature.Detect(content) is not { } contentType)
            {
                skipped.Add(new SkippedAttachment(index, fileName, DraftUploadLimits.UnsupportedType));
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

        return new PreparedAttachments(stored, skipped);
    }

    // A display name only: any path and control characters are dropped, and it is never used as a storage path.
    public static string SafeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        name = new string([.. name.Where(character => !char.IsControl(character))]).Trim();
        return name.Length == 0 ? "attachment" : name;
    }

    public static async Task UploadAsync(IIntakeBlobStore blobs, Guid submissionId, IEnumerable<StoredAttachment> stored, CancellationToken cancellationToken)
    {
        foreach (var attachment in stored)
        {
            await blobs.UploadAsync(
                submissionId, attachment.Record.Id, new MemoryStream(attachment.Content, writable: false), attachment.Record.ContentType, cancellationToken);
        }
    }

    // Best effort: a blob that cannot be removed now is private and unreferenced, and the orphan sweep removes it later.
    public static async Task DeleteBlobsAsync(IIntakeBlobStore blobs, Guid submissionId, int storedCount, ILogger logger)
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
