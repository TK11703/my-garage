using MyGarage.Shared.Reminders;

namespace MyGarage.Shared.Dashboard;

// The garage at a glance. New metrics arrive as new properties; clients ignore the ones they do not know.
public sealed class DashboardDto
{
    public required int Vehicles { get; init; }

    public required int ServiceRecords { get; init; }

    public required int FuelEnergyEntries { get; init; }

    public required int DraftsAwaitingReview { get; init; }

    public required int RemindersOverdue { get; init; }

    public required int RemindersDueSoon { get; init; }

    // Up to five reminders needing attention: overdue first, then due soon.
    public required IReadOnlyList<ReminderDto> Attention { get; init; }

    // Fuel and charging cost for each of the last 12 calendar months, oldest first, including months with nothing spent.
    public IReadOnlyList<FuelEnergySpendMonthDto> FuelEnergySpend { get; init; } = [];

    // The last 10 journal entries across the user's vehicles, newest first.
    public IReadOnlyList<RecentActivityDto> RecentActivity { get; init; } = [];
}
