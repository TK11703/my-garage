using System.Globalization;
using MyGarage.Shared.Reminders;

namespace MyGarage.Web.Reminders;

public static class ReminderLinks
{
    // The vehicle page opened on its Reminders tab.
    public static string Tab(Guid vehicleId) => $"/vehicles/{vehicleId}?tab=reminders";

    public static string Tab(Guid vehicleId, string saved) => $"/vehicles/{vehicleId}?tab=reminders&saved={saved}";

    public static string New(Guid vehicleId, string? preset = null) =>
        $"/vehicles/{vehicleId}/reminders/new" + (preset is null ? string.Empty : $"?preset={preset}");

    // The add form opened with a suggestion's values; the owner still reviews and saves it.
    public static string New(Guid vehicleId, SuggestedReminderDto suggestion)
    {
        var query = new List<string> { $"name={Uri.EscapeDataString(suggestion.Name)}" };
        if (suggestion.IntervalMiles is { } miles)
        {
            query.Add($"miles={miles.ToString(CultureInfo.InvariantCulture)}");
        }

        if (suggestion.IntervalMonths is { } months)
        {
            query.Add($"months={months.ToString(CultureInfo.InvariantCulture)}");
        }

        query.Add($"doneOn={suggestion.LastDoneOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        if (suggestion.LastDoneMileage is { } mileage)
        {
            query.Add($"mileage={mileage.ToString(CultureInfo.InvariantCulture)}");
        }

        return $"/vehicles/{vehicleId}/reminders/new?{string.Join('&', query)}";
    }

    public static string Detail(Guid vehicleId, Guid reminderId) => $"/vehicles/{vehicleId}/reminders/{reminderId}";

    public static string Detail(Guid vehicleId, Guid reminderId, string saved) => $"/vehicles/{vehicleId}/reminders/{reminderId}?saved={saved}";

    public static string Edit(Guid vehicleId, Guid reminderId) => $"/vehicles/{vehicleId}/reminders/{reminderId}/edit";
}
