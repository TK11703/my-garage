namespace MyGarage.Web.Reminders;

public static class ReminderMessages
{
    // The notice for a "saved" query value; anything else is ignored.
    public static string? Saved(string? value) => value switch
    {
        "created" => "Reminder added.",
        "updated" => "Reminder updated.",
        "paused" => "Reminder paused. It shows no status and sends no notifications until you resume it.",
        "resumed" => "Reminder resumed.",
        "deleted" => "Reminder deleted.",
        _ => null,
    };
}
