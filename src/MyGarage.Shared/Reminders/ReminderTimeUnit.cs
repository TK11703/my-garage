using System.Text.Json.Serialization;
using MyGarage.Shared.Drafts;

namespace MyGarage.Shared.Reminders;

[JsonConverter(typeof(SnakeCaseEnumConverter<ReminderTimeUnit>))]
public enum ReminderTimeUnit
{
    Months,
    Years,
}
