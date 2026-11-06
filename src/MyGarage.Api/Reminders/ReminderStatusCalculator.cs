using MyGarage.Shared.Reminders;

namespace MyGarage.Api.Reminders;

public sealed record ReminderCalculationInput(
    int? IntervalMiles,
    int? IntervalMonths,
    DateOnly LastDoneOn,
    int? LastDoneMileage,
    bool IsPaused,
    int? CurrentMileage);

public sealed record ReminderCalculation(
    ReminderStatus Status,
    int? NextDueMileage,
    DateOnly? NextDueDate,
    int? MilesRemaining,
    int? DaysRemaining);

// The single definition of next due and status; every read path and the notifier use it.
public static class ReminderStatusCalculator
{
    public static ReminderCalculation Calculate(ReminderCalculationInput input, DateOnly today)
    {
        int? nextMileage = input is { IntervalMiles: { } miles, LastDoneMileage: { } lastMileage } ? lastMileage + miles : null;
        DateOnly? nextDate = input.IntervalMonths is { } months ? input.LastDoneOn.AddMonths(months) : null;
        int? milesRemaining = nextMileage is { } due && input.CurrentMileage is { } current ? due - current : null;
        int? daysRemaining = nextDate is { } dueDate ? dueDate.DayNumber - today.DayNumber : null;

        return new ReminderCalculation(
            StatusOf(input, nextMileage, milesRemaining, daysRemaining),
            nextMileage,
            nextDate,
            milesRemaining,
            daysRemaining);
    }

    private static ReminderStatus StatusOf(ReminderCalculationInput input, int? nextMileage, int? milesRemaining, int? daysRemaining)
    {
        if (input.IsPaused)
        {
            return ReminderStatus.Paused;
        }

        if (nextMileage is not null && milesRemaining is null && daysRemaining is null)
        {
            return ReminderStatus.MileageNeeded;
        }

        var status = ReminderStatus.Upcoming;
        if (milesRemaining is { } miles)
        {
            status = MoreUrgent(status, Classify(miles, ReminderThresholds.DueSoonMiles));
        }

        if (daysRemaining is { } days)
        {
            status = MoreUrgent(status, Classify(days, ReminderThresholds.DueSoonDays));
        }

        return status;
    }

    private static ReminderStatus Classify(int remaining, int dueSoonLimit) =>
        remaining <= 0 ? ReminderStatus.Overdue : remaining <= dueSoonLimit ? ReminderStatus.DueSoon : ReminderStatus.Upcoming;

    // Overdue > DueSoon > Upcoming.
    private static ReminderStatus MoreUrgent(ReminderStatus a, ReminderStatus b) =>
        a == ReminderStatus.Overdue || b == ReminderStatus.Overdue ? ReminderStatus.Overdue
        : a == ReminderStatus.DueSoon || b == ReminderStatus.DueSoon ? ReminderStatus.DueSoon
        : ReminderStatus.Upcoming;
}
