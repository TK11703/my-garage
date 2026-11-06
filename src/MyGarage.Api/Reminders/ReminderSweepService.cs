using Microsoft.Extensions.Options;

namespace MyGarage.Api.Reminders;

// Bound from Reminders:Sweep. Tests turn the loop off and call ReminderSweepService.RunOnceAsync themselves.
public sealed class ReminderSweepOptions
{
    public const string SectionName = "Reminders:Sweep";

    // False stops the loop from running at all.
    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 60;
}

// Hourly backstop for the notifier: catches date-based changes and any write that does not check reminders itself.
public sealed class ReminderSweepService(
    ReminderEvaluator evaluator,
    IOptions<ReminderSweepOptions> options,
    ILogger<ReminderSweepService> logger) : BackgroundService
{
    public static TimeSpan IntervalOf(ReminderSweepOptions settings) => TimeSpan.FromMinutes(Math.Max(1, settings.IntervalMinutes));

    // Returns how many notifications were raised in this run.
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var raised = await evaluator.EvaluateAllAsync(cancellationToken);
        if (raised > 0)
        {
            logger.LogInformation("The reminder sweep raised {Count} notifications", raised);
        }

        return raised;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("The reminder sweep is disabled by configuration");
            return;
        }

        var interval = IntervalOf(settings);
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
                logger.LogError("The reminder sweep hit {ExceptionType}; it will try again", ex.GetType().Name);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
