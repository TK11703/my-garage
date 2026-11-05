using Microsoft.Extensions.Options;
using MyGarage.Database.Tags;

namespace MyGarage.Api.Vehicles.Tags;

// Gives the shared features that still have no category one, in a single model call. Never throws for a service or database failure:
// the features stay pending (shown under Other) and the background sweep tries again.
public sealed class FeatureCategorizationService(
    VehicleTagRepository tags,
    IFeatureCategorizer categorizer,
    IOptions<FeatureCategorizationOptions> options,
    ILogger<FeatureCategorizationService> logger)
{
    public int BatchSize => Math.Max(1, options.Value.BatchSize);

    // Returns how many features were categorized.
    public async Task<int> CategorizePendingAsync(CancellationToken cancellationToken)
    {
        if (!categorizer.IsEnabled)
        {
            return 0;
        }

        try
        {
            var pending = await tags.ListUncategorizedAsync(BatchSize, cancellationToken);
            if (pending.Count == 0)
            {
                return 0;
            }

            var result = await categorizer.CategorizeAsync([.. pending.Select(feature => feature.Name)], cancellationToken);
            if (result.IsFailure)
            {
                return 0;
            }

            var categories = new Dictionary<Guid, string>();
            for (var i = 0; i < pending.Count && i < result.Categories.Count; i++)
            {
                if (result.Categories[i] is { } category)
                {
                    categories[pending[i].Id] = category;
                }
            }

            await tags.SetCategoriesAsync(categories, cancellationToken);
            return categories.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Categorizing features failed; they stay pending");
            return 0;
        }
    }
}

// Backfills features saved before categories existed and retries any the inline call missed.
public sealed class FeatureCategorizationSweep(
    FeatureCategorizationService service,
    IOptions<FeatureCategorizationOptions> options,
    TimeProvider timeProvider) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.SweepEnabled)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.SweepMinutes));
        try
        {
            await Task.Delay(StartupDelay, timeProvider, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                int done;
                do
                {
                    done = await service.CategorizePendingAsync(stoppingToken);
                }
                while (done >= service.BatchSize && !stoppingToken.IsCancellationRequested);

                await Task.Delay(interval, timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The host is stopping.
        }
    }
}
