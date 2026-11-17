using MyGarage.Shared.Reminders;

namespace MyGarage.Shared.Vehicles;

// The most urgent active reminder of a vehicle, for its garage card.
public sealed class VehicleNextDueDto
{
    public required Guid ReminderId { get; init; }

    public required string Name { get; init; }

    public required ReminderStatus Status { get; init; }

    // Negative when overdue; null without a mile interval or a known mileage.
    public int? MilesRemaining { get; init; }

    // Negative when overdue; null without a time interval.
    public int? DaysRemaining { get; init; }
}
