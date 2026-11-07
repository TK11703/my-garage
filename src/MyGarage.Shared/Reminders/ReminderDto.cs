namespace MyGarage.Shared.Reminders;

public class ReminderDto
{
    public required Guid Id { get; init; }

    public required Guid VehicleId { get; init; }

    public required string VehicleName { get; init; }

    public required string Name { get; init; }

    public int? IntervalMiles { get; init; }

    public ReminderTimeIntervalDto? IntervalTime { get; init; }

    public required DateOnly LastDoneOn { get; init; }

    public int? LastDoneMileage { get; init; }

    public required bool IsPaused { get; init; }

    public required ReminderStatus Status { get; init; }

    public int? NextDueMileage { get; init; }

    public DateOnly? NextDueDate { get; init; }

    // Negative when overdue; null without a distance interval or a current mileage.
    public int? MilesRemaining { get; init; }

    // Negative when overdue; null without a time interval.
    public int? DaysRemaining { get; init; }
}
