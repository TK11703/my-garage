using System.Text.Json;
using Microsoft.Extensions.Options;
using MyGarage.Api.Common;
using MyGarage.Api.Drafts;
using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Database.Drafts;
using MyGarage.Database.EmailIntake;
using MyGarage.Shared.Drafts;

namespace MyGarage.Api.EmailIntake;

// Claims queued submissions and turns each into draft maintenance items for the matched owner (never records).
// Each stored attachment is read on its own and becomes its own draft; the email text is context for the files and is read as the
// source only when there are none. Retries after 1, 5, then 30 minutes (the third failed attempt ends a text-only submission, and
// turns attachments that still cannot be read into manual-entry drafts). Logs carry ids, counts, statuses, and durations only:
// never email text, file names, extracted values, or exception messages, which can quote data.
public sealed class SubmissionProcessor(
    EmailSubmissionRepository submissions,
    MaintenanceDraftRepository drafts,
    IIntakeBlobStore blobs,
    IDraftExtractor extractor,
    TimeProvider timeProvider,
    IOptions<SubmissionProcessingOptions> options,
    ILogger<SubmissionProcessor> logger) : BackgroundService
{
    public const int MaxAttempts = 3;

    // A "none" answer below this confidence is treated as an unreadable file rather than a file that is not a maintenance document.
    private const decimal ReadableConfidence = 0.2m;

    private static readonly TimeSpan[] RetryBackoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)];

    // Processes one due submission; false when none was due. Tests call this directly with the host's loop disabled.
    public async Task<bool> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var submission = await submissions.ClaimNextAsync(now, TimeSpan.FromMinutes(Math.Max(1, options.Value.LeaseMinutes)), cancellationToken);
        if (submission is null)
        {
            return false;
        }

        var started = timeProvider.GetTimestamp();
        string outcome;
        try
        {
            outcome = await ProcessAsync(submission, now, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down: the lease expires and another worker picks the submission up.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Submission {SubmissionId} attempt {Attempt} failed with {ExceptionType}", submission.Id, submission.AttemptCount, ex.GetType().Name);
            outcome = await FailAttemptAsync(submission, "service_error", cancellationToken);
        }

        EmailIntakeMetrics.SubmissionsProcessed.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        logger.LogInformation(
            "Submission {SubmissionId} attempt {Attempt} finished as {Outcome} in {ElapsedMs} ms",
            submission.Id,
            submission.AttemptCount,
            outcome,
            (long)timeProvider.GetElapsedTime(started).TotalMilliseconds);
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Email submission processing is disabled by configuration");
            return;
        }

        var pause = TimeSpan.FromSeconds(Math.Max(1, settings.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = false;
            try
            {
                processed = await ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError("The email submission loop hit {ExceptionType}; it will try again", ex.GetType().Name);
            }

            if (!processed)
            {
                try
                {
                    await Task.Delay(pause, timeProvider, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    // Returns the outcome: ready, not_applicable, retry, or failed.
    private async Task<string> ProcessAsync(EmailSubmissionRecord submission, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (submission.OwnerId is not { Length: > 0 } ownerId)
        {
            await submissions.FailAsync(submission.Id, "no_owner", now, cancellationToken);
            return "failed";
        }

        // Ownership is proved in SQL: only attachments stored for this submission under this owner come back.
        var attachments = await submissions.ListAttachmentsAsync(submission.Id, ownerId, cancellationToken);
        var skippedReasons = ReadSkippedReasons(submission.SkippedAttachments);

        return attachments.Count == 0
            ? await ProcessTextAsync(submission, ownerId, skippedReasons, now, cancellationToken)
            : await ProcessAttachmentsAsync(submission, ownerId, attachments, skippedReasons, now, cancellationToken);
    }

    // No stored attachments: the email text is the only source.
    private async Task<string> ProcessTextAsync(
        EmailSubmissionRecord submission, string ownerId, IReadOnlyList<string> skippedReasons, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Nothing to read: extraction is off, or there are no words (only unsupported files, or an empty message).
        var noWords = string.IsNullOrWhiteSpace(submission.BodyText) && (skippedReasons.Count > 0 || string.IsNullOrWhiteSpace(submission.Subject));
        if (!extractor.IsEnabled || noWords)
        {
            await submissions.CompleteAsync(submission.Id, "not_applicable", now, cancellationToken);
            return "not_applicable";
        }

        var result = await ExtractAsync(submission.Id, new ExtractionInput(submission.Subject, submission.BodyText, submission.ReceivedAt), cancellationToken);
        if (result.IsFailure)
        {
            return await FailAttemptAsync(submission, result.FailureReason!, cancellationToken);
        }

        // The model's answer is untrusted: it only ever becomes a draft the owner must approve.
        if (ProposalMapper.Map(result) is not { } proposal)
        {
            await submissions.CompleteAsync(submission.Id, "not_applicable", now, cancellationToken);
            return "not_applicable";
        }

        var vehicles = await drafts.ListOwnerVehiclesForMatchingAsync(ownerId, cancellationToken);
        var draft = NewDraft(vehicles, result, proposal, null, SkippedNote(skippedReasons), submission.PreselectedVehicleId);
        return await CreateDraftsAsync(submission, ownerId, [draft], now, cancellationToken);
    }

    // One draft per attachment that is a maintenance document, a manual-entry draft for each file that cannot be read (research R9).
    private async Task<string> ProcessAttachmentsAsync(
        EmailSubmissionRecord submission,
        string ownerId,
        IReadOnlyList<EmailAttachmentRecord> attachments,
        IReadOnlyList<string> skippedReasons,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var readings = new List<AttachmentReading>(attachments.Count);
        var refused = new List<string>(skippedReasons);
        foreach (var attachment in attachments)
        {
            var reading = await ReadAttachmentAsync(submission, attachment, cancellationToken);
            if (reading.Outcome == AttachmentOutcome.Refused)
            {
                refused.Add(reading.RefusalReason!);
            }

            readings.Add(reading);
        }

        // Only a submission whose every readable file failed to be read is worth another try; nothing has been created yet.
        var readable = readings.Where(reading => reading.Outcome != AttachmentOutcome.Refused).ToList();
        if (readable.Count > 0 && readable.All(reading => reading.Outcome == AttachmentOutcome.TransientFailure) && submission.AttemptCount < MaxAttempts)
        {
            return await FailAttemptAsync(submission, readable[0].Result!.FailureReason!, cancellationToken);
        }

        var vehicles = await drafts.ListOwnerVehiclesForMatchingAsync(ownerId, cancellationToken);
        var skippedNote = SkippedNote(refused);
        var created = new List<NewMaintenanceDraft>();
        var withoutDraft = new List<EmailAttachmentRecord>();
        foreach (var reading in readings)
        {
            switch (reading.Outcome)
            {
                case AttachmentOutcome.Draft:
                    created.Add(NewDraft(vehicles, reading.Result!, reading.Proposal!, reading.Attachment.Id, skippedNote, submission.PreselectedVehicleId));
                    break;
                case AttachmentOutcome.ManualEntry or AttachmentOutcome.TransientFailure:
                    created.Add(NewManualEntryDraft(vehicles, reading.Result, reading.Attachment.Id, skippedNote, submission.PreselectedVehicleId));
                    break;
                default:
                    withoutDraft.Add(reading.Attachment);
                    break;
            }
        }

        if (created.Count == 0)
        {
            await submissions.CompleteAsync(submission.Id, "not_applicable", now, cancellationToken);
            await DeleteIntakeFilesAsync(submission.Id, withoutDraft, cancellationToken);
            return "not_applicable";
        }

        var outcome = await CreateDraftsAsync(submission, ownerId, created, now, cancellationToken);
        if (outcome == "ready")
        {
            // Files with a draft stay until that draft is decided or expires; the rest have no use.
            await DeleteIntakeFilesAsync(submission.Id, withoutDraft, cancellationToken);
        }

        return outcome;
    }

    // Reads one stored file: opens it, confirms it is still the type recorded at intake, and asks the extractor about it.
    private async Task<AttachmentReading> ReadAttachmentAsync(EmailSubmissionRecord submission, EmailAttachmentRecord attachment, CancellationToken cancellationToken)
    {
        await using var content = await blobs.OpenReadAsync(submission.Id, attachment.Id, cancellationToken);
        if (content is null)
        {
            logger.LogWarning("Attachment {AttachmentId} of submission {SubmissionId} has no stored file", attachment.Id, submission.Id);
            return AttachmentReading.Refuse(attachment, "file_missing");
        }

        // The recorded type came from the bytes at intake; the bytes are checked again because they are about to be sent on.
        if (await DetectTypeAsync(content, cancellationToken) != attachment.ContentType)
        {
            logger.LogWarning("Attachment {AttachmentId} of submission {SubmissionId} no longer matches its recorded type", attachment.Id, submission.Id);
            return AttachmentReading.Refuse(attachment, "unsupported_type");
        }

        if (!extractor.IsEnabled)
        {
            return AttachmentReading.ManualEntry(attachment, null);
        }

        var input = new ExtractionInput(
            submission.Subject, submission.BodyText, submission.ReceivedAt, new ExtractionDocument(content, attachment.ContentType, attachment.FileName));
        var result = await ExtractAsync(submission.Id, input, cancellationToken);

        if (result.IsFailure)
        {
            return result.FailureReason == "content_filter"
                ? AttachmentReading.ManualEntry(attachment, result)
                : AttachmentReading.Transient(attachment, result);
        }

        // The model's answer is untrusted: it only ever becomes a draft the owner must approve.
        if (ProposalMapper.Map(result) is { } proposal)
        {
            return AttachmentReading.Draft(attachment, result, proposal);
        }

        return result.Confidence is { } confidence && confidence >= ReadableConfidence
            ? AttachmentReading.NotMaintenance(attachment)
            : AttachmentReading.ManualEntry(attachment, result);
    }

    // The type of the file's leading bytes, with the stream put back at its start (a one-pass stream is opened afresh by the caller's retry).
    private async Task<string?> DetectTypeAsync(Stream content, CancellationToken cancellationToken)
    {
        var header = new byte[16];
        var filled = 0;
        while (filled < header.Length)
        {
            var read = await content.ReadAsync(header.AsMemory(filled), cancellationToken);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        if (content.CanSeek)
        {
            content.Position = 0;
        }
        else
        {
            logger.LogWarning("An intake file could not be read from its start again");
            return null;
        }

        return FileSignature.Detect(header.AsSpan(0, filled));
    }

    private async Task<string> CreateDraftsAsync(
        EmailSubmissionRecord submission, string ownerId, IReadOnlyList<NewMaintenanceDraft> created, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await drafts.CreateDraftsAsync(submission.Id, ownerId, created, now, cancellationToken))
        {
            return "ready";
        }

        await submissions.FailAsync(submission.Id, "owner_mismatch", now, cancellationToken);
        return "failed";
    }

    private static NewMaintenanceDraft NewDraft(
        IReadOnlyList<VehicleMatchCandidate> vehicles, ExtractionResult result, DraftProposal proposal, Guid? attachmentId, string? skippedNote, Guid? preselectedVehicleId)
    {
        var match = VehicleMatcher.Match(vehicles, result.VehicleHint, preselectedVehicleId);
        return new NewMaintenanceDraft(
            Guid.CreateVersion7(),
            DraftKinds.ToText(DraftState.NeedsReview),
            DraftKinds.ToText(proposal.Kind!.Value),
            match.VehicleId,
            DraftKinds.ToText(match.Match),
            ConfidenceOf(result),
            DraftProposalJson.Serialize(proposal),
            WithSkippedNote(ProposalMapper.NotesFor(result), skippedNote),
            attachmentId);
    }

    // No kind and an empty proposal: the owner picks the type and types the values from the original file.
    private static NewMaintenanceDraft NewManualEntryDraft(
        IReadOnlyList<VehicleMatchCandidate> vehicles, ExtractionResult? result, Guid attachmentId, string? skippedNote, Guid? preselectedVehicleId)
    {
        var match = VehicleMatcher.Match(vehicles, result?.VehicleHint, preselectedVehicleId);
        return new NewMaintenanceDraft(
            Guid.CreateVersion7(),
            DraftKinds.ToText(DraftState.ManualEntry),
            null,
            match.VehicleId,
            DraftKinds.ToText(match.Match),
            result is null ? null : ConfidenceOf(result),
            "{}",
            WithSkippedNote(result is null ? null : ProposalMapper.NotesFor(result), skippedNote),
            attachmentId);
    }

    private static decimal? ConfidenceOf(ExtractionResult result) =>
        result.Confidence is { } confidence ? decimal.Round(Math.Clamp(confidence, 0m, 1m), 2) : null;

    // Reasons only (never file names): "1 attachment was skipped: unsupported type".
    private static string? SkippedNote(IReadOnlyList<string> reasons)
    {
        if (reasons.Count == 0)
        {
            return null;
        }

        var why = string.Join(", ", reasons.Select(reason => reason.Replace('_', ' ')).Distinct(StringComparer.Ordinal));
        return reasons.Count == 1 ? $"1 attachment was skipped: {why}" : $"{reasons.Count} attachments were skipped: {why}";
    }

    // The skipped-files line always survives the draft's 500-character notes limit; the model's remark gives way first.
    private static string? WithSkippedNote(string? notes, string? skippedNote)
    {
        if (skippedNote is null)
        {
            return notes;
        }

        var room = ProposalMapper.MaxNotesLength - skippedNote.Length - 1;
        if (string.IsNullOrWhiteSpace(notes) || room <= 0)
        {
            return skippedNote.Length <= ProposalMapper.MaxNotesLength ? skippedNote : skippedNote[..ProposalMapper.MaxNotesLength];
        }

        var remark = notes.Length <= room ? notes : notes[..room].TrimEnd();
        return $"{remark} {skippedNote}";
    }

    private static List<string> ReadSkippedReasons(string json)
    {
        try
        {
            var entries = JsonSerializer.Deserialize<List<SkippedEntry>>(json, JsonSerializerOptions.Web) ?? [];
            return [.. entries.Select(entry => string.IsNullOrWhiteSpace(entry.Reason) ? "unreadable" : entry.Reason)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // Best effort: a file that cannot be deleted now is logged by id and left behind.
    private async Task DeleteIntakeFilesAsync(Guid submissionId, IReadOnlyList<EmailAttachmentRecord> attachments, CancellationToken cancellationToken)
    {
        foreach (var attachment in attachments)
        {
            try
            {
                await blobs.DeleteAsync(submissionId, attachment.Id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Intake file {AttachmentId} of submission {SubmissionId} could not be deleted: {ExceptionType}", attachment.Id, submissionId, ex.GetType().Name);
            }
        }
    }

    private async Task<ExtractionResult> ExtractAsync(Guid submissionId, ExtractionInput input, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        ExtractionResult result;
        try
        {
            result = await extractor.ExtractAsync(input, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Extraction for submission {SubmissionId} threw {ExceptionType}", submissionId, ex.GetType().Name);
            result = ExtractionResult.Failed("service_error");
        }

        EmailIntakeMetrics.ExtractionDuration.Record(
            timeProvider.GetElapsedTime(started).TotalMilliseconds,
            new KeyValuePair<string, object?>("outcome", result.IsFailure ? result.FailureReason : "ok"));
        return result;
    }

    private sealed record SkippedEntry(string? FileName, string? Reason);

    private enum AttachmentOutcome
    {
        // A maintenance document with a proposal.
        Draft,

        // Confidently not a maintenance document: no draft.
        NotMaintenance,

        // Could not be read (or not tried): a draft with no kind.
        ManualEntry,

        // The service failed; retried when it is the same for every file.
        TransientFailure,

        // Not the file it was stored as, or gone: dropped and reported as skipped.
        Refused,
    }

    private sealed record AttachmentReading(
        EmailAttachmentRecord Attachment, AttachmentOutcome Outcome, ExtractionResult? Result = null, DraftProposal? Proposal = null, string? RefusalReason = null)
    {
        public static AttachmentReading Draft(EmailAttachmentRecord attachment, ExtractionResult result, DraftProposal proposal) => new(attachment, AttachmentOutcome.Draft, result, proposal);

        public static AttachmentReading NotMaintenance(EmailAttachmentRecord attachment) => new(attachment, AttachmentOutcome.NotMaintenance);

        public static AttachmentReading ManualEntry(EmailAttachmentRecord attachment, ExtractionResult? result) => new(attachment, AttachmentOutcome.ManualEntry, result);

        public static AttachmentReading Transient(EmailAttachmentRecord attachment, ExtractionResult result) => new(attachment, AttachmentOutcome.TransientFailure, result);

        public static AttachmentReading Refuse(EmailAttachmentRecord attachment, string reason) => new(attachment, AttachmentOutcome.Refused, RefusalReason: reason);
    }

    // One failed attempt: back to the queue after 1, then 5 minutes; the third attempt gives up.
    private async Task<string> FailAttemptAsync(EmailSubmissionRecord submission, string reason, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (submission.AttemptCount >= MaxAttempts)
        {
            await submissions.FailAsync(submission.Id, reason, now, cancellationToken);
            return "failed";
        }

        var delay = RetryBackoff[Math.Clamp(submission.AttemptCount - 1, 0, RetryBackoff.Length - 1)];
        await submissions.RetryAsync(submission.Id, now + delay, reason, cancellationToken);
        return "retry";
    }
}
