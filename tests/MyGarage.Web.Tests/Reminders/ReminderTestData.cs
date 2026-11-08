using MyGarage.Shared.Reminders;

namespace MyGarage.Web.Tests.Reminders;

public static class ReminderTestData
{
    public static ReminderDto Reminder(
        Guid? vehicleId = null,
        Guid? id = null,
        string name = "Oil change",
        int? intervalMiles = 5_000,
        ReminderTimeIntervalDto? intervalTime = null,
        DateOnly? lastDoneOn = null,
        int? lastDoneMileage = 40_000,
        bool isPaused = false,
        ReminderStatus status = ReminderStatus.Upcoming,
        int? nextDueMileage = 45_000,
        DateOnly? nextDueDate = null,
        int? milesRemaining = 3_000,
        int? daysRemaining = null,
        string vehicleName = "2020 Ford F-150",
        bool vehicleArchived = false) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            VehicleId = vehicleId ?? Guid.NewGuid(),
            VehicleName = vehicleName,
            Name = name,
            IntervalMiles = intervalMiles,
            IntervalTime = intervalTime,
            LastDoneOn = lastDoneOn ?? new DateOnly(2026, 5, 1),
            LastDoneMileage = lastDoneMileage,
            IsPaused = isPaused,
            VehicleArchived = vehicleArchived,
            Status = status,
            NextDueMileage = nextDueMileage,
            NextDueDate = nextDueDate,
            MilesRemaining = milesRemaining,
            DaysRemaining = daysRemaining,
        };

    public static ReminderDetailDto DetailOf(ReminderDto reminder, params ReminderCompletionDto[] completions) =>
        new()
        {
            Id = reminder.Id,
            VehicleId = reminder.VehicleId,
            VehicleName = reminder.VehicleName,
            Name = reminder.Name,
            IntervalMiles = reminder.IntervalMiles,
            IntervalTime = reminder.IntervalTime,
            LastDoneOn = reminder.LastDoneOn,
            LastDoneMileage = reminder.LastDoneMileage,
            IsPaused = reminder.IsPaused,
            VehicleArchived = reminder.VehicleArchived,
            Status = reminder.Status,
            NextDueMileage = reminder.NextDueMileage,
            NextDueDate = reminder.NextDueDate,
            MilesRemaining = reminder.MilesRemaining,
            DaysRemaining = reminder.DaysRemaining,
            Completions = completions,
        };

    public static ReminderCompletionDto Completion(DateOnly doneOn, int? mileage = null) =>
        new() { Id = Guid.NewGuid(), DoneOn = doneOn, Mileage = mileage };
}
