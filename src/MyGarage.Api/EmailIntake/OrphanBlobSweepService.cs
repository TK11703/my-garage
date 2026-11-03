using Microsoft.Extensions.Options;
using MyGarage.Database.EmailIntake;

namespace MyGarage.Api.EmailIntake;

// Hourly: intake uploads blobs before it commits rows, so a crash or a lost race can leave a blob no row points to.
// Blobs older than an hour with no attachment row are removed. It stops with the processing switch (EmailIntake:Processing:Enabled).
public sealed class OrphanBlobSweepService(
    EmailSubmissionRepository submissions,
    IIntakeBlobStore blobs,
    TimeProvider timeProvider,
    IOptions<SubmissionProcessingOptions> options,
    ILogger<OrphanBlobSweepService> logger) : BackgroundService
{
    public static readonly TimeSpan MinimumAge = TimeSpan.FromHours(1);

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    // Returns how many blobs were deleted in this run.
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - MinimumAge;
        var deleted = 0;
        foreach (var name in await blobs.ListBlobNamesOlderThanAsync(cutoff, cancellationToken))
        {
            // Only {submissionId}/{attachmentId} names are ours to judge; anything else is left alone.
            var parts = name.Split('/');
            if (parts.Length != 2 || !Guid.TryParse(parts[0], out var submissionId) || !Guid.TryParse(parts[1], out var attachmentId))
            {
                continue;
            }

            if (await submissions.AttachmentExistsAsync(submissionId, attachmentId, cancellationToken))
            {
                continue;
            }

            await blobs.DeleteAsync(submissionId, attachmentId, cancellationToken);
            deleted++;
        }

        if (deleted > 0)
        {
            logger.LogInformation("Deleted {Count} intake files that no attachment row refers to", deleted);
        }

        return deleted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("The orphan blob sweep is disabled by configuration");
            return;
        }

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
                logger.LogError("The orphan blob sweep hit {ExceptionType}; it will try again", ex.GetType().Name);
            }

            try
            {
                await Task.Delay(Interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
