using System.Text.Json.Serialization;
using MyGarage.Shared.Drafts;

namespace MyGarage.Shared.Reminders;

// Derived on every read from the reminder, the vehicle's current mileage, and today; never stored.
[JsonConverter(typeof(SnakeCaseEnumConverter<ReminderStatus>))]
public enum ReminderStatus
{
    Upcoming,
    DueSoon,
    Overdue,
    Paused,
    MileageNeeded,
}
