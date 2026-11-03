using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MyGarage.Api.EmailIntake;
using MyGarage.Database.Drafts;
using MyGarage.Database.EmailIntake;

namespace MyGarage.Api.Drafts;

// Bound from Drafts:Expiry. Tests turn the background loop off and call DraftExpiryService.RunOnceAsync themselves.
public sealed class DraftExpiryOptions
{
    public const string SectionName = "Drafts:Expiry";

    // False stops the loop from running at all.
    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 60;
}

// Hourly: expires drafts nobody reviewed within 30 days, removes their email text and notifications, then their stored files.
// A file that cannot be deleted now (storage down) stays on a retry list and is tried again on the next run.
public sealed class DraftExpiryService(
    MaintenanceDraftRepository drafts,
    IIntakeBlobStore intake,
    TimeProvider timeProvider,
    IOptions<DraftExpiryOptions> options,
    ILogger<DraftExpiryService> logger) : BackgroundService
{
    private readonly ConcurrentQueue<DraftAttachmentRef> pendingDeletes = new();

    // Returns how many drafts were expired in this run.
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var expired = await drafts.ExpireDueAsync(timeProvider.GetUtcNow(), cancellationToken);
        foreach (var attachment in expired.Attachments)
        {
            pendingDeletes.Enqueue(attachment);
        }

        var failed = await DeletePendingFilesAsync(cancellationToken);

        if (expired.Count > 0)
        {
            EmailIntakeMetrics.DraftsDecided.Add(expired.Count, new KeyValuePair<string, object?>("decision", "expired"));
            logger.LogInformation("Expired {Count} drafts; {Pending} stored files are waiting to be deleted", expired.Count, failed);
        }

        return expired.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Draft expiry is disabled by configuration");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, settings.IntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError("The draft expiry run hit {ExceptionType}; it will try again", ex.GetType().Name);
            }

            try
            {
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // Returns how many files could not be deleted and were put back for the next run.
    private async Task<int> DeletePendingFilesAsync(CancellationToken cancellationToken)
    {
        var retry = new List<DraftAttachmentRef>();
        var count = pendingDeletes.Count;
        for (var index = 0; index < count && pendingDeletes.TryDequeue(out var attachment); index++)
        {
            try
            {
                await intake.DeleteAsync(attachment.SubmissionId, attachment.AttachmentId, cancellationToken);
            }
            catch (IntakeStorageUnavailableException ex)
            {
                logger.LogWarning(ex, "Could not delete the stored file {AttachmentId} of submission {SubmissionId}; it will be retried", attachment.AttachmentId, attachment.SubmissionId);
                retry.Add(attachment);
            }
        }

        foreach (var attachment in retry)
        {
            pendingDeletes.Enqueue(attachment);
        }

        return retry.Count;
    }
}
