namespace MyGarage.Database.Reminders;

// The vehicle's name is composed from these parts in the Api; the vehicle has no stored name.
public record ReminderRecord
{
    public Guid Id { get; init; }

    public Guid VehicleId { get; init; }

    public int? VehicleYear { get; init; }

    public string VehicleMake { get; init; } = string.Empty;

    public string VehicleModel { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int? IntervalMiles { get; init; }

    public int? IntervalMonths { get; init; }

    public DateOnly LastDoneOn { get; init; }

    public int? LastDoneMileage { get; init; }

    public bool IsPaused { get; init; }

    // due_soon or overdue: the last state the owner was told about.
    public string? NotifiedState { get; init; }

    // The greatest of the latest-dated service record's odometer, the vehicle's latest odometer reading, and its purchase mileage.
    public int? CurrentMileage { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
