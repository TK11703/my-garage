using System.Text.Json.Serialization;
using MyGarage.Shared.Drafts;

namespace MyGarage.Shared.Reminders;

[JsonConverter(typeof(SnakeCaseEnumConverter<ReminderAppliesTo>))]
public enum ReminderAppliesTo
{
    Vehicle,
    Equipment,
    Location,
}
