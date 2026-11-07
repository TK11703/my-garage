using MyGarage.Database.Reminders;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Reminders;

// Data records stay inside the Api; every response is built here.
public static class ReminderMapper
{
    public static ReminderDto ToDto(ReminderRecord record, DateOnly today)
    {
        var calculation = Calculate(record, today);
        return new ReminderDto
        {
            Id = record.Id,
            VehicleId = record.VehicleId,
            VehicleName = VehicleName.Compose(record.VehicleYear, record.VehicleMake, record.VehicleModel),
            Name = record.Name,
            IntervalMiles = record.IntervalMiles,
            IntervalTime = ToIntervalTime(record.IntervalMonths),
            LastDoneOn = record.LastDoneOn,
            LastDoneMileage = record.LastDoneMileage,
            IsPaused = record.IsPaused,
            Status = calculation.Status,
            NextDueMileage = calculation.NextDueMileage,
            NextDueDate = calculation.NextDueDate,
            MilesRemaining = calculation.MilesRemaining,
            DaysRemaining = calculation.DaysRemaining,
        };
    }

    public static ReminderDetailDto ToDetailDto(ReminderRecord record, IReadOnlyList<ReminderCompletionRecord> completions, DateOnly today)
    {
        var dto = ToDto(record, today);
        return new ReminderDetailDto
        {
            Id = dto.Id,
            VehicleId = dto.VehicleId,
            VehicleName = dto.VehicleName,
            Name = dto.Name,
            IntervalMiles = dto.IntervalMiles,
            IntervalTime = dto.IntervalTime,
            LastDoneOn = dto.LastDoneOn,
            LastDoneMileage = dto.LastDoneMileage,
            IsPaused = dto.IsPaused,
            Status = dto.Status,
            NextDueMileage = dto.NextDueMileage,
            NextDueDate = dto.NextDueDate,
            MilesRemaining = dto.MilesRemaining,
            DaysRemaining = dto.DaysRemaining,
            Completions = [.. completions.Select(completion => new ReminderCompletionDto
            {
                Id = completion.Id,
                DoneOn = completion.DoneOn,
                Mileage = completion.Mileage,
            })],
        };
    }

    // The request is validated before this is called, so required values are present.
    public static ReminderValues ToValues(ReminderRequest request) =>
        new(
            request.Name!.Trim(),
            request.IntervalMiles,
            request.IntervalTime is { } interval ? ToMonths(interval) : null,
            request.LastDoneOn!.Value,
            request.LastDoneMileage);

    // Shown in years when the months divide by 12.
    public static ReminderTimeIntervalDto? ToIntervalTime(int? months) => months switch
    {
        null => null,
        { } value when value % 12 == 0 => new ReminderTimeIntervalDto { Value = value / 12, Unit = ReminderTimeUnit.Years },
        { } value => new ReminderTimeIntervalDto { Value = value, Unit = ReminderTimeUnit.Months },
    };

    public static int ToMonths(ReminderTimeIntervalDto interval) => interval.Unit == ReminderTimeUnit.Years ? interval.Value * 12 : interval.Value;

    internal static ReminderCalculation Calculate(ReminderRecord record, DateOnly today) =>
        ReminderStatusCalculator.Calculate(
            new ReminderCalculationInput(
                record.IntervalMiles, record.IntervalMonths, record.LastDoneOn, record.LastDoneMileage, record.IsPaused, record.CurrentMileage),
            today);
}
