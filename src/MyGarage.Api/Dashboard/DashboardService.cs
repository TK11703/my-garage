using MyGarage.Api.Reminders;
using MyGarage.Database.Dashboard;
using MyGarage.Database.Reminders;
using MyGarage.Shared.Dashboard;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Dashboard;

// Reminder figures come from the same calculator as every other view, so the dashboard never disagrees with the reminder lists.
public sealed class DashboardService(DashboardRepository dashboard, ReminderRepository reminders, TimeProvider timeProvider)
{
    private const int AttentionLimit = 5;
    private const int SpendMonths = 12;

    public async Task<DashboardDto> GetAsync(string ownerId, CancellationToken cancellationToken)
    {
        var counts = await dashboard.GetCountsAsync(ownerId, cancellationToken);
        var today = ReminderRules.Today(timeProvider);
        var all = (await reminders.ListForOwnerAsync(ownerId, cancellationToken)).Select(item => ReminderMapper.ToDto(item, today)).ToList();
        var attention = ReminderOrdering.MostUrgentFirst(all.Where(item => item.Status is ReminderStatus.Overdue or ReminderStatus.DueSoon));

        return new DashboardDto
        {
            Vehicles = counts.Vehicles,
            ServiceRecords = counts.ServiceRecords,
            FuelEnergyEntries = counts.FuelEnergyEntries,
            DraftsAwaitingReview = counts.DraftsAwaitingReview,
            RemindersOverdue = all.Count(item => item.Status == ReminderStatus.Overdue),
            RemindersDueSoon = all.Count(item => item.Status == ReminderStatus.DueSoon),
            Attention = [.. attention.Take(AttentionLimit)],
            FuelEnergySpend = await GetFuelEnergySpendAsync(ownerId, today, cancellationToken),
        };
    }

    private async Task<IReadOnlyList<FuelEnergySpendMonthDto>> GetFuelEnergySpendAsync(string ownerId, DateOnly today, CancellationToken cancellationToken)
    {
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var first = thisMonth.AddMonths(1 - SpendMonths);
        var rows = await dashboard.GetFuelEnergySpendAsync(ownerId, first, thisMonth.AddMonths(1).AddDays(-1), cancellationToken);

        return [.. Enumerable.Range(0, SpendMonths).Select(offset =>
        {
            var month = first.AddMonths(offset);
            var vehicles = rows.Where(row => row.Month == month).GroupBy(row => row.VehicleId)
                .Select(group => new FuelEnergySpendVehicleDto
                {
                    VehicleId = group.Key,
                    VehicleName = VehicleName.Compose(group.First().VehicleYear, group.First().VehicleMake, group.First().VehicleModel),
                    Fuel = group.Where(row => row.Kind == "fuel").Sum(row => row.Total),
                    Charging = group.Where(row => row.Kind == "energy").Sum(row => row.Total),
                })
                .ToList();
            return new FuelEnergySpendMonthDto
            {
                Month = month,
                Fuel = vehicles.Sum(vehicle => vehicle.Fuel),
                Charging = vehicles.Sum(vehicle => vehicle.Charging),
                Vehicles = vehicles,
            };
        })];
    }
}
