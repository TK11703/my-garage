namespace MyGarage.Web.Reminders;

public static class ReminderLinks
{
    // The vehicle page opened on its Reminders tab.
    public static string Tab(Guid vehicleId) => $"/vehicles/{vehicleId}?tab=reminders";

    public static string Tab(Guid vehicleId, string saved) => $"/vehicles/{vehicleId}?tab=reminders&saved={saved}";

    public static string New(Guid vehicleId, string? preset = null) =>
        $"/vehicles/{vehicleId}/reminders/new" + (preset is null ? string.Empty : $"?preset={preset}");

    public static string Detail(Guid vehicleId, Guid reminderId) => $"/vehicles/{vehicleId}/reminders/{reminderId}";

    public static string Detail(Guid vehicleId, Guid reminderId, string saved) => $"/vehicles/{vehicleId}/reminders/{reminderId}?saved={saved}";

    public static string Edit(Guid vehicleId, Guid reminderId) => $"/vehicles/{vehicleId}/reminders/{reminderId}/edit";
}
