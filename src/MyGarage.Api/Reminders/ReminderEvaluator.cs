using MyGarage.Database.Reminders;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Reminders;

// Compares each active reminder's computed state with the state the owner was last told about and raises one notification per change.
public class ReminderEvaluator(ReminderRepository reminders, TimeProvider timeProvider, ILogger<ReminderEvaluator> logger)
{
    private const string DueSoon = "due_soon";
    private const string Overdue = "overdue";
    private const int MaxTitleLength = 120;

    // Returns how many notifications were raised.
    public virtual async Task<int> EvaluateVehicleAsync(string ownerId, Guid vehicleId, CancellationToken cancellationToken) =>
        await EvaluateAsync(await reminders.ListForEvaluationAsync(ownerId, vehicleId, cancellationToken), cancellationToken);

    public async Task<int> EvaluateOwnerAsync(string ownerId, CancellationToken cancellationToken) =>
        await EvaluateAsync(await reminders.ListForEvaluationAsync(ownerId, null, cancellationToken), cancellationToken);

    public async Task<int> EvaluateAllAsync(CancellationToken cancellationToken) =>
        await EvaluateAsync(await reminders.ListForEvaluationAsync(null, null, cancellationToken), cancellationToken);

    // For the writes that can change a status: a failure here is logged by type and never fails the write.
    public async Task TryEvaluateVehicleAsync(string ownerId, Guid vehicleId, CancellationToken cancellationToken)
    {
        try
        {
            await EvaluateVehicleAsync(ownerId, vehicleId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError("The reminder check after a change hit {ExceptionType}; the sweep will try again", ex.GetType().Name);
        }
    }

    private async Task<int> EvaluateAsync(IReadOnlyList<ReminderForEvaluationRecord> items, CancellationToken cancellationToken)
    {
        var today = ReminderRules.Today(timeProvider);
        var now = timeProvider.GetUtcNow();
        var raised = 0;

        foreach (var item in items)
        {
            var computed = ComputedState(ReminderMapper.Calculate(item, today).Status);
            if (Rank(computed) > Rank(item.NotifiedState))
            {
                var kind = computed == Overdue ? "reminder_overdue" : "reminder_due_soon";
                if (await reminders.InsertNotificationAndAdvanceAsync(
                        item.OwnerId, item.Id, item.NotifiedState, computed!, Guid.CreateVersion7(), kind, Title(item, computed!), now, cancellationToken))
                {
                    raised++;
                    ReminderMetrics.Notified.Add(1, new KeyValuePair<string, object?>("state", computed));
                }
            }
            else if (computed != item.NotifiedState)
            {
                // Eased (mileage corrected, interval edited) or back to Upcoming: remember it without notifying.
                await reminders.TryAdvanceNotifiedStateAsync(item.Id, item.NotifiedState, computed, cancellationToken);
            }
        }

        ReminderMetrics.Evaluated.Add(items.Count);
        return raised;
    }

    private static string? ComputedState(ReminderStatus status) => status switch
    {
        ReminderStatus.Overdue => Overdue,
        ReminderStatus.DueSoon => DueSoon,
        _ => null,
    };

    private static int Rank(string? state) => state switch
    {
        Overdue => 2,
        DueSoon => 1,
        _ => 0,
    };

    // "Hitch check (Trailer hitch) on 2020 Ford F-150 is overdue", cut to the column limit.
    private static string Title(ReminderForEvaluationRecord item, string state)
    {
        var what = item.Subject is { Length: > 0 } subject ? $"{item.Name} ({subject})" : item.Name;
        var vehicle = VehicleName.Compose(item.VehicleYear, item.VehicleMake, item.VehicleModel);
        var title = $"{what} on {vehicle} is {(state == Overdue ? "overdue" : "due soon")}";
        return title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;
    }
}
